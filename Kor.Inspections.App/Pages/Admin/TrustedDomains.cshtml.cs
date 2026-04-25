using Kor.Inspections.App.Data;
using Kor.Inspections.App.Data.Models;
using Kor.Inspections.App.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kor.Inspections.App.Pages.Admin
{
    public class TrustedDomainsModel : PageModel
    {
        private readonly InspectionsContext _db;
        private readonly ILogger<TrustedDomainsModel> _logger;

        public TrustedDomainsModel(InspectionsContext db, ILogger<TrustedDomainsModel> logger)
        {
            _db = db;
            _logger = logger;
        }

        public IList<TrustedDomainRow> TrustedDomains { get; private set; } = new List<TrustedDomainRow>();

        [TempData]
        public string? StatusMessage { get; set; }

        public sealed class TrustedDomainRow
        {
            public int Id { get; init; }
            public string ProjectNumber { get; init; } = string.Empty;
            public string EmailDomain { get; init; } = string.Empty;
            public DateTime ApprovedUtc { get; init; }
            public DateTime ExpiresUtc { get; init; }
            public bool IsExpired { get; init; }
        }

        public async Task OnGetAsync()
        {
            var raw = await _db.ProjectDefaults
                .AsNoTracking()
                .OrderBy(x => x.ProjectNumber)
                .ThenBy(x => x.EmailDomain)
                .ToListAsync();

            var nowUtc = DateTime.UtcNow;
            TrustedDomains = raw
                .Select(x =>
                {
                    var expiresUtc = ProjectBootstrapVerificationService.GetExplicitDomainApprovalExpirationUtc(x.UpdatedUtc);
                    return new TrustedDomainRow
                    {
                        Id = x.Id,
                        ProjectNumber = x.ProjectNumber,
                        EmailDomain = x.EmailDomain,
                        ApprovedUtc = x.UpdatedUtc,
                        ExpiresUtc = expiresUtc,
                        IsExpired = expiresUtc < nowUtc
                    };
                })
                .ToList();
        }

        public async Task<IActionResult> OnPostRevokeAsync(int id)
        {
            var row = await _db.ProjectDefaults.FirstOrDefaultAsync(x => x.Id == id);
            if (row == null)
            {
                StatusMessage = "Trusted domain was not found.";
                return RedirectToPage();
            }

            _db.ProjectDefaults.Remove(row);
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to revoke trusted domain. Id={Id} ProjectNumber={ProjectNumber} EmailDomain={EmailDomain}",
                    row.Id, row.ProjectNumber, row.EmailDomain);
                StatusMessage = "Could not revoke this trusted domain. Please try again.";
                return RedirectToPage();
            }

            _logger.LogInformation(
                "Revoked trusted domain. ProjectNumber={ProjectNumber} EmailDomain={EmailDomain} RevokedBy={RevokedBy}",
                row.ProjectNumber,
                row.EmailDomain,
                PageContext?.HttpContext?.User?.Identity?.Name ?? "unknown");

            StatusMessage = $"Revoked explicit domain approval for {row.EmailDomain} on project {row.ProjectNumber}.";
            return RedirectToPage();
        }
    }
}
