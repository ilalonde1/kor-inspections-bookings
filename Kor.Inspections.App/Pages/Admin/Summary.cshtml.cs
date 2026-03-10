using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Kor.Inspections.App.Data;
using Kor.Inspections.App.Data.Models;
using Kor.Inspections.App.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Kor.Inspections.App.Pages.Admin
{
    public class SummaryModel : PageModel
    {
        private readonly InspectionsContext _db;
        private readonly TimeRuleService _timeRules;
        private readonly GraphMailService _mail;

        public SummaryModel(
            InspectionsContext db,
            TimeRuleService timeRules,
            GraphMailService mail)
        {
            _db = db;
            _timeRules = timeRules;
            _mail = mail;
        }

        public DateTime SummaryDateLocal { get; private set; }

        public IList<SummaryRow> Bookings { get; private set; } = new List<SummaryRow>();

        [BindProperty(SupportsGet = true)]
        public string? Sort { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Dir { get; set; }

        // Needed for dropdown
        public IList<Inspector> Inspectors { get; private set; } = new List<Inspector>();

        [TempData]
        public string? StatusMessage { get; set; }

        public class SummaryRow
        {
            public Guid BookingId { get; set; }

            public DateTime StartLocal { get; set; }
            public DateTime EndLocal { get; set; }
            public string? TimePreference { get; set; }
            public string ProjectNumber { get; set; } = string.Empty;
            public string ProjectAddress { get; set; } = string.Empty;

            public string ContactName { get; set; } = string.Empty;
            public string ContactPhone { get; set; } = string.Empty;

            public string Status { get; set; } = string.Empty;
            public string AssignedTo { get; set; } = string.Empty;

            public string Comments { get; set; } = string.Empty;
        }

        public async Task OnGetAsync()
        {
            var tz = _timeRules.TimeZone;

            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            var tomorrowLocal = nowLocal.Date.AddDays(1);

            SummaryDateLocal = tomorrowLocal;

            var startUtc = TimeZoneInfo.ConvertTimeToUtc(tomorrowLocal, tz);
            var endUtc = TimeZoneInfo.ConvertTimeToUtc(tomorrowLocal.AddDays(1), tz);

            var query = _db.Bookings
                .Where(b =>
                    b.StartUtc >= startUtc &&
                    b.StartUtc < endUtc &&
                    b.Status != "Cancelled");

            query = (Sort?.ToLower(), Dir?.ToLower()) switch
            {
                ("job", "desc") => query.OrderByDescending(b => b.ProjectNumber),
                ("job", _) => query.OrderBy(b => b.ProjectNumber),
                ("address", "desc") => query.OrderByDescending(b => b.ProjectAddress),
                ("address", _) => query.OrderBy(b => b.ProjectAddress),
                ("status", "desc") => query.OrderByDescending(b => b.Status),
                ("status", _) => query.OrderBy(b => b.Status),
                ("assigned", "desc") => query.OrderByDescending(b => b.AssignedTo),
                ("assigned", _) => query.OrderBy(b => b.AssignedTo),
                ("time", "desc") => query.OrderByDescending(b => b.StartUtc),
                _ => query.OrderBy(b => b.StartUtc),
            };

            var bookings = await query.ToListAsync();

            Bookings = bookings
                .Select(b =>
                {
                    var startLocalRow = TimeZoneInfo.ConvertTimeFromUtc(b.StartUtc, tz);
                    var endLocalRow = TimeZoneInfo.ConvertTimeFromUtc(b.EndUtc, tz);

                    return new SummaryRow
                    {
                        BookingId = b.BookingId,
                        StartLocal = startLocalRow,
                        EndLocal = endLocalRow,
                        TimePreference = b.TimePreference,
                        ProjectNumber = b.ProjectNumber,
                        ProjectAddress = b.ProjectAddress ?? string.Empty,
                        ContactName = b.ContactName,
                        ContactPhone = b.ContactPhone,
                        Status = b.Status,
                        AssignedTo = string.IsNullOrWhiteSpace(b.AssignedTo)
                            ? "Unassigned"
                            : b.AssignedTo,
                        Comments = b.Comments ?? string.Empty
                    };
                })
                .ToList();

            // Load inspectors for dropdown
            Inspectors = await _db.Inspectors
                .Where(i => i.Enabled)
                .OrderBy(i => i.DisplayName)
                .ToListAsync();
        }

        // ---------------------------------------------------
        // FULL SUMMARY EMAIL
        // ---------------------------------------------------
        public async Task<IActionResult> OnPostEmailAsync()
        {
            await OnGetAsync();

            var fromMailbox = "reviews@korstructural.com";
            var toEmail = "reviews@korstructural.com";
            var subject = $"Kor Field Reviews - {SummaryDateLocal:yyyy-MM-dd} (Tomorrow)";
            var html = BuildEmailHtml(SummaryDateLocal, Bookings);

            await _mail.SendHtmlAsync(fromMailbox, toEmail, subject, html);

            StatusMessage = $"Summary email sent to {toEmail}.";
            return RedirectToPage();
        }

        // ---------------------------------------------------
        // INSPECTOR SUMMARY EMAIL
        // ---------------------------------------------------
        public async Task<IActionResult> OnPostEmailInspectorAsync(string inspectorEmail)
        {
            if (string.IsNullOrWhiteSpace(inspectorEmail))
                return RedirectToPage();

            await OnGetAsync();

            var inspector = await _db.Inspectors
    .AsNoTracking()
    .FirstOrDefaultAsync(i => i.Email == inspectorEmail && i.Enabled);

var inspectorBookings = (inspector is null
    ? Enumerable.Empty<SummaryRow>()
    : Bookings.Where(b =>
        !string.Equals(b.AssignedTo, "Unassigned", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(b.AssignedTo, inspector.DisplayName, StringComparison.Ordinal)))
    .ToList();

            if (!inspectorBookings.Any())
            {
                StatusMessage = "No bookings found for that inspector.";
                return RedirectToPage();
            }

            var fromMailbox = "reviews@korstructural.com";
            var subject = $"Your Field Reviews - {SummaryDateLocal:yyyy-MM-dd}";
            var html = BuildEmailHtml(SummaryDateLocal, inspectorBookings);

            await _mail.SendHtmlAsync(fromMailbox, inspectorEmail, subject, html);

            StatusMessage = $"Inspector summary sent to {inspectorEmail}.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostEmailAllInspectorsAsync()
        {
            await OnGetAsync();

            var fromMailbox = "reviews@korstructural.com";

            var inspectorsByName = Inspectors
                .Where(i => !string.IsNullOrWhiteSpace(i.DisplayName) && !string.IsNullOrWhiteSpace(i.Email))
                .GroupBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Email, StringComparer.OrdinalIgnoreCase);

            var groups = Bookings
                .Where(b =>
                    !string.IsNullOrWhiteSpace(b.AssignedTo) &&
                    !string.Equals(b.AssignedTo, "Unassigned", StringComparison.OrdinalIgnoreCase))
                .GroupBy(b => b.AssignedTo, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var sentCount = 0;

            foreach (var group in groups)
            {
                var assignedName = group.Key ?? string.Empty;
                if (!inspectorsByName.TryGetValue(assignedName, out var inspectorEmail))
                    continue;

                var inspectorBookings = group.ToList();
                if (inspectorBookings.Count == 0)
                    continue;

                var subject = $"Your Field Reviews - {SummaryDateLocal:yyyy-MM-dd}";
                var html = BuildEmailHtml(SummaryDateLocal, inspectorBookings);

                await _mail.SendHtmlAsync(fromMailbox, inspectorEmail, subject, html);
                sentCount++;
            }

            if (sentCount == 0)
            {
                StatusMessage = "No assigned inspector bookings found to email.";
            }
            else
            {
                StatusMessage = $"Inspector summaries sent to {sentCount} inspector(s).";
            }

            return RedirectToPage();
        }

        private static string BuildEmailHtml(DateTime dateLocal, IList<SummaryRow> rows)
        {
            var sb = new StringBuilder();

            sb.Append("<h2>Field Reviews for ")
              .Append(WebUtility.HtmlEncode(dateLocal.ToString("yyyy-MM-dd")))
              .Append("</h2>");

            if (rows.Count == 0)
            {
                sb.Append("<p>No bookings are scheduled.</p>");
                return sb.ToString();
            }

            sb.Append("<table border='1' cellpadding='6' cellspacing='0' ")
              .Append("style='border-collapse:collapse;font-family:Segoe UI,Arial,sans-serif;font-size:13px;'>");

            sb.Append("<thead><tr style='background:#3F5364;color:#ffffff;'>")
              .Append("<th>Time</th>")
              .Append("<th>Job</th>")
              .Append("<th>Site Address</th>")
              .Append("<th>Contact</th>")
              .Append("<th>Assigned</th>")
              .Append("<th>Status</th>")
              .Append("<th>Comments</th>")
              .Append("</tr></thead><tbody>");

            foreach (var b in rows.OrderBy(r => r.StartLocal))
            {
                var timeText = $"{b.StartLocal:HH:mm} - {b.EndLocal:HH:mm}";
                var contactText = $"{b.ContactName} ({b.ContactPhone})";

                sb.Append("<tr>")
                  .Append($"<td>{WebUtility.HtmlEncode(timeText)}</td>")
                  .Append($"<td>{WebUtility.HtmlEncode(b.ProjectNumber)}</td>")
                  .Append($"<td>{WebUtility.HtmlEncode(b.ProjectAddress)}</td>")
                  .Append($"<td>{WebUtility.HtmlEncode(contactText)}</td>")
                  .Append($"<td>{WebUtility.HtmlEncode(b.AssignedTo)}</td>")
                  .Append($"<td>{WebUtility.HtmlEncode(b.Status)}</td>")
                  .Append($"<td>{WebUtility.HtmlEncode(b.Comments)}</td>")
                  .Append("</tr>");
            }

            sb.Append("</tbody></table>");
            return sb.ToString();
        }

        public string ToggleDir(string column)
        {
            if (Sort?.Equals(column, StringComparison.OrdinalIgnoreCase) == true
                && Dir == "asc")
                return "desc";

            return "asc";
        }
    }
}



