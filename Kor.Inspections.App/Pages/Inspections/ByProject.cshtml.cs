using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Kor.Inspections.App.Pages.Inspections
{
    public class ByProjectModel : PageModel
    {
        [BindProperty(SupportsGet = true)]
        public string ProjectNumber { get; set; } = string.Empty;

        [BindProperty(SupportsGet = true)]
        public string Email { get; set; } = string.Empty;

        public bool HasRequiredQuery =>
            !string.IsNullOrWhiteSpace(ProjectNumber) &&
            !string.IsNullOrWhiteSpace(Email);
    }
}
