using Kor.Inspections.App.Data;
using Kor.Inspections.App.Data.Models;
using Kor.Inspections.App.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Kor.Inspections.App.Pages.Admin
{
    public class TrustedDomainsModel : PageModel
    {
        private readonly InspectionsContext _db;

        public TrustedDomainsModel(InspectionsContext db)
        {
            _db = db;
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
            TrustedDomains = await _db.ProjectDefaults
                .AsNoTracking()
                .OrderBy(x => x.ProjectNumber)
                .ThenBy(x => x.EmailDomain)
                .Select(x => new TrustedDomainRow
                {
                    Id = x.Id,
                    ProjectNumber = x.ProjectNumber,
                    EmailDomain = x.EmailDomain,
                    ApprovedUtc = x.UpdatedUtc,
                    ExpiresUtc = ProjectBootstrapVerificationService.GetExplicitDomainApprovalExpirationUtc(x.UpdatedUtc),
                    IsExpired = ProjectBootstrapVerificationService.GetExplicitDomainApprovalExpirationUtc(x.UpdatedUtc) < DateTime.UtcNow
                })
                .ToListAsync();
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
            await _db.SaveChangesAsync();

            StatusMessage = $"Revoked explicit domain approval for {row.EmailDomain} on project {row.ProjectNumber}.";
            return RedirectToPage();
        }
    }
}
