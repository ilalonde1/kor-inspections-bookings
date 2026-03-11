using Kor.Inspections.App.Data;
using Kor.Inspections.App.Data.Models;
using Kor.Inspections.App.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Kor.Inspections.App.Pages.Admin
{
    public class TrustedDomainsModel : PageModel
    {
        private readonly InspectionsContext _db;
        private readonly IMemoryCache _cache;

        public TrustedDomainsModel(InspectionsContext db, IMemoryCache cache)
        {
            _db = db;
            _cache = cache;
        }

        public IList<ProjectDefault> TrustedDomains { get; private set; } = new List<ProjectDefault>();

        [TempData]
        public string? StatusMessage { get; set; }

        public async Task OnGetAsync()
        {
            TrustedDomains = await _db.ProjectDefaults
                .AsNoTracking()
                .OrderBy(x => x.ProjectNumber)
                .ThenBy(x => x.EmailDomain)
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

            _cache.Remove(ProjectCacheKeys.BuildVerificationKey(row.ProjectNumber, row.EmailDomain));
            StatusMessage = $"Revoked trust for {row.EmailDomain} on project {row.ProjectNumber}.";
            return RedirectToPage();
        }
    }
}
