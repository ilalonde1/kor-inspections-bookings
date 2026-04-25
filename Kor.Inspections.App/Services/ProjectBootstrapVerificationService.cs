using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Kor.Inspections.App.Data;
using Kor.Inspections.App.Data.Models;
using Kor.Inspections.App.Options;
using Microsoft.Extensions.Options;

namespace Kor.Inspections.App.Services
{
    /// <summary>
    /// Email-OTP verification for the public booking flow. Verification state
    /// is persisted in the ProjectVerifications table (one row per
    /// project+email) so it survives IIS app-pool recycles and is safe under
    /// multi-instance deployment. Expiry is driven by ExpiresUtc on each row.
    /// </summary>
    public class ProjectBootstrapVerificationService
    {
        private static readonly TimeSpan PendingTtl = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan VerifiedTtl = TimeSpan.FromHours(8);
        private static readonly TimeSpan ExplicitDomainTrustTtl = TimeSpan.FromDays(30);
        internal static TimeSpan ExplicitDomainApprovalTtl => ExplicitDomainTrustTtl;
        private const int MaxFailedAttempts = 5;

        private readonly GraphMailService _mailService;
        private readonly NotificationOptions _notificationOptions;
        private readonly ILogger<ProjectBootstrapVerificationService> _logger;
        private readonly InspectionsContext _db;

        public ProjectBootstrapVerificationService(
            GraphMailService mailService,
            IOptions<NotificationOptions> notificationOptions,
            InspectionsContext db,
            ILogger<ProjectBootstrapVerificationService> logger)
        {
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

            var now = DateTime.UtcNow;

            var activeVerified = await _db.ProjectVerifications
                .AsNoTracking()
                .AnyAsync(v =>
                    v.ProjectNumber == normalizedProject &&
                    v.Email == normalizedEmail &&
                    v.Verified &&
                    v.ExpiresUtc > now,
                    ct);

            if (activeVerified)
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

            var now = DateTime.UtcNow;
            var existing = await _db.ProjectVerifications
                .FirstOrDefaultAsync(v =>
                    v.ProjectNumber == normalizedProject &&
                    v.Email == normalizedEmail,
                    ct);

            string code;
            if (existing != null &&
                !existing.Verified &&
                existing.FailedAttempts == 0 &&
                existing.ExpiresUtc > now)
            {
                // Reuse the existing pending code and refresh its window.
                // Matches pre-refactor UX: clicking "Send code" twice quickly
                // does not spam a new code each time.
                code = existing.Code;
                existing.ExpiresUtc = now + PendingTtl;
                existing.UpdatedUtc = now;
            }
            else
            {
                code = RandomNumberGenerator.GetInt32(0, 1000000).ToString("D6");
                if (existing != null)
                {
                    existing.Code = code;
                    existing.Verified = false;
                    existing.FailedAttempts = 0;
                    existing.ExpiresUtc = now + PendingTtl;
                    existing.UpdatedUtc = now;
                }
                else
                {
                    _db.ProjectVerifications.Add(new ProjectVerification
                    {
                        ProjectNumber = normalizedProject,
                        Email = normalizedEmail,
                        Code = code,
                        Verified = false,
                        FailedAttempts = 0,
                        ExpiresUtc = now + PendingTtl,
                        CreatedUtc = now,
                        UpdatedUtc = now
                    });
                }
            }

            await _db.SaveChangesAsync(ct);

            try
            {
                var subject = "KOR verification code";
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

            var now = DateTime.UtcNow;
            var row = await _db.ProjectVerifications
                .FirstOrDefaultAsync(v =>
                    v.ProjectNumber == normalizedProject &&
                    v.Email == normalizedEmail,
                    ct);

            if (row == null || row.ExpiresUtc <= now)
                return false;

            if (!string.Equals(row.Code, normalizedCode, StringComparison.Ordinal))
            {
                row.FailedAttempts++;
                row.UpdatedUtc = now;

                if (row.FailedAttempts >= MaxFailedAttempts)
                {
                    // Expire immediately — caller must request a new code.
                    row.ExpiresUtc = now;
                }

                await _db.SaveChangesAsync(ct);
                return false;
            }

            row.Verified = true;
            row.FailedAttempts = 0;
            row.ExpiresUtc = now + VerifiedTtl;
            row.UpdatedUtc = now;
            await _db.SaveChangesAsync(ct);

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
