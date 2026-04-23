using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Kor.Inspections.App.Data;
using Kor.Inspections.App.Options;
using Microsoft.Extensions.Options;

namespace Kor.Inspections.App.Services
{
    public class ProjectBootstrapVerificationService
    {
        private static readonly TimeSpan PendingTtl = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan VerifiedTtl = TimeSpan.FromHours(8);
        private static readonly TimeSpan ExplicitDomainTrustTtl = TimeSpan.FromDays(30);
        internal static TimeSpan ExplicitDomainApprovalTtl => ExplicitDomainTrustTtl;

        private readonly IMemoryCache _cache;
        private readonly GraphMailService _mailService;
        private readonly NotificationOptions _notificationOptions;
        private readonly ILogger<ProjectBootstrapVerificationService> _logger;
        private readonly InspectionsContext _db;

        private sealed class VerificationState
        {
            public string Code { get; set; } = string.Empty;
            public bool Verified { get; set; }
            public int FailedAttempts { get; set; }
        }

        public ProjectBootstrapVerificationService(
            IMemoryCache cache,
            GraphMailService mailService,
            IOptions<NotificationOptions> notificationOptions,
            InspectionsContext db,
            ILogger<ProjectBootstrapVerificationService> logger)
        {
            _cache = cache;
            _mailService = mailService;
            _notificationOptions = notificationOptions.Value;
            _db = db;
            _logger = logger;
        }

        public async Task<(bool RequiresVerification, bool IsVerified)> GetStatusAsync(
            string projectNumber,
            string email,
            CancellationToken ct = default)
        {
            var normalizedProject = NormalizeProject(projectNumber);
            var normalizedEmail = NormalizeEmail(email);

            if (string.IsNullOrWhiteSpace(normalizedProject) || string.IsNullOrWhiteSpace(normalizedEmail))
                return (false, false);

            var key = BuildUserVerificationKey(normalizedProject, normalizedEmail);

            if (_cache.TryGetValue<VerificationState>(key, out var state) && state is { Verified: true })
                return (true, true);

            if (await HasExplicitDomainApprovalAsync(normalizedProject, normalizedEmail, ct))
                return (true, true);

            return (true, false);
        }

        public async Task<bool> SendCodeAsync(
            string projectNumber,
            string email,
            CancellationToken ct = default)
        {
            var normalizedProject = NormalizeProject(projectNumber);
            var normalizedEmail = NormalizeEmail(email);

            if (string.IsNullOrWhiteSpace(normalizedProject) || string.IsNullOrWhiteSpace(normalizedEmail))
                return false;

            var status = await GetStatusAsync(normalizedProject, normalizedEmail, ct);
            if (!status.RequiresVerification || status.IsVerified)
                return true;

            var key = BuildUserVerificationKey(normalizedProject, normalizedEmail);

            string code;
            if (_cache.TryGetValue<VerificationState>(key, out var existing)
                && existing is { Verified: false }
                && existing.FailedAttempts == 0)
            {
                code = existing.Code;
            }
            else
            {
                code = RandomNumberGenerator.GetInt32(0, 1000000).ToString("D6");

                _cache.Set(
                    key,
                    new VerificationState
                    {
                        Code = code,
                        Verified = false,
                        FailedAttempts = 0
                    },
                    PendingTtl);
            }

            try
            {
                var subject = $"KOR verification code: {code}";
                var body =
                    "<p>Use this verification code to access project contacts:</p>" +
                    $"<p><strong>{code}</strong></p>" +
                    $"<p>Project: {normalizedProject}</p>" +
                    "<p>This code expires in 15 minutes.</p>";

                await _mailService.SendHtmlAsync(
                    _notificationOptions.FromMailbox,
                    normalizedEmail,
                    subject,
                    body);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to send project bootstrap verification email for project {ProjectNumber} to {Email}.",
                    normalizedProject,
                    normalizedEmail);

                return false;
            }
        }

        public async Task<bool> VerifyCodeAsync(
            string projectNumber,
            string email,
            string code,
            CancellationToken ct = default)
        {
            var normalizedProject = NormalizeProject(projectNumber);
            var normalizedEmail = NormalizeEmail(email);
            var normalizedCode = (code ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(normalizedProject) ||
                string.IsNullOrWhiteSpace(normalizedEmail) ||
                string.IsNullOrWhiteSpace(normalizedCode))
            {
                return false;
            }

            if (await HasExplicitDomainApprovalAsync(normalizedProject, normalizedEmail, ct))
                return true;

            var key = BuildUserVerificationKey(normalizedProject, normalizedEmail);

            if (!_cache.TryGetValue<VerificationState>(key, out var state) || state == null)
                return false;

            if (!string.Equals(state.Code, normalizedCode, StringComparison.Ordinal))
            {
                state.FailedAttempts++;
                if (state.FailedAttempts >= 5)
                {
                    _cache.Remove(key);
                }
                else
                {
                    _cache.Set(key, state, PendingTtl);
                }

                return false;
            }

            state.Verified = true;
            state.FailedAttempts = 0;
            _cache.Set(key, state, VerifiedTtl);

            return true;
        }

        public async Task<bool> EnsureVerifiedForProjectAccessAsync(
            string projectNumber,
            string email,
            CancellationToken ct = default)
        {
            var status = await GetStatusAsync(projectNumber, email, ct);
            return !status.RequiresVerification || status.IsVerified;
        }

        private async Task<bool> HasExplicitDomainApprovalAsync(
            string normalizedProject,
            string normalizedEmail,
            CancellationToken ct)
        {
            var domain = GetEmailDomain(normalizedEmail);
            if (string.IsNullOrWhiteSpace(domain))
                return false;

            var approval = await _db.ProjectDefaults
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.ProjectNumber == normalizedProject && x.EmailDomain == domain,
                    ct);

            if (approval == null)
                return false;

            var expiresAtUtc = GetExplicitDomainApprovalExpirationUtc(approval.UpdatedUtc);
            if (expiresAtUtc < DateTime.UtcNow)
            {
                _logger.LogInformation(
                    "Explicit domain approval expired for project {ProjectNumber} and domain {EmailDomain} on {ExpiresAtUtc}.",
                    normalizedProject,
                    domain,
                    expiresAtUtc);
                return false;
            }

            _logger.LogInformation(
                "Using explicit domain approval for project {ProjectNumber}, domain {EmailDomain}, and user {Email}.",
                normalizedProject,
                domain,
                normalizedEmail);

            return true;
        }

        internal static DateTime GetExplicitDomainApprovalExpirationUtc(DateTime approvedUtc)
        {
            return approvedUtc.Add(ExplicitDomainTrustTtl);
        }

        private static string BuildUserVerificationKey(string projectNumber, string email)
        {
            return ProjectCacheKeys.BuildVerificationKey(projectNumber, email);
        }

        private static string GetEmailDomain(string email)
        {
            var at = email.LastIndexOf('@');
            return (at > 0 && at < email.Length - 1)
                ? email[(at + 1)..].Trim().ToLowerInvariant()
                : string.Empty;
        }

        private static string NormalizeProject(string projectNumber)
        {
            var base5 = ProjectNumberHelper.Base5(projectNumber);
            return string.IsNullOrWhiteSpace(base5)
                ? (projectNumber ?? string.Empty).Trim()
                : base5.Trim();
        }

        private static string NormalizeEmail(string email)
        {
            var v = (email ?? string.Empty).Trim().ToLowerInvariant();
            var at = v.LastIndexOf('@');
            return (at > 0 && at < v.Length - 1) ? v : string.Empty;
        }

    }
}
