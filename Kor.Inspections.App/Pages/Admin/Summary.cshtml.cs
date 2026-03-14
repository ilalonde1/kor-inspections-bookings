using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Kor.Inspections.App.Data;
using Kor.Inspections.App.Data.Models;
using Kor.Inspections.App.Options;
using Kor.Inspections.App.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Inspections.App.Pages.Admin
{
    public class SummaryModel : PageModel
    {
        private readonly InspectionsContext _db;
        private readonly TimeRuleService _timeRules;
        private readonly GraphMailService _mail;
        private readonly NotificationOptions _notificationOptions;
        private readonly ILogger<SummaryModel> _logger;

        public SummaryModel(
            InspectionsContext db,
            TimeRuleService timeRules,
            GraphMailService mail,
            IOptions<NotificationOptions> notificationOptions,
            ILogger<SummaryModel> logger)
        {
            _db = db;
            _timeRules = timeRules;
            _mail = mail;
            _notificationOptions = notificationOptions.Value;
            _logger = logger;
        }

        public DateTime SummaryDateLocal { get; private set; }
        public DateTime SelectedDate { get; private set; }

        public IList<SummaryRow> Bookings { get; private set; } = new List<SummaryRow>();

        [BindProperty(SupportsGet = true)]
        public string? Date { get; set; }

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
            public int? RouteOrder { get; set; }
            public string? TimePreference { get; set; }
            public string ProjectNumber { get; set; } = string.Empty;
            public string ProjectAddress { get; set; } = string.Empty;

            public string ContactName { get; set; } = string.Empty;
            public string ContactPhone { get; set; } = string.Empty;

            public string Status { get; set; } = string.Empty;
            public string AssignedTo { get; set; } = string.Empty;
            public string? AssignedToValue { get; set; }

            public string Comments { get; set; } = string.Empty;
        }

        public sealed class RouteOrderRequest
        {
            public string? InspectorEmail { get; set; }
            public string? OrderedBookingIds { get; set; }
        }

        public async Task OnGetAsync()
        {
            ViewData["Title"] = "Summary";
            var tz = _timeRules.TimeZone;
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            var defaultDate = nowLocal.Date.AddDays(1);

            if (!DateTime.TryParseExact(
                    Date,
                    "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var selectedDate))
            {
                selectedDate = defaultDate;
            }

            SelectedDate = selectedDate.Date;
            Date = SelectedDate.ToString("yyyy-MM-dd");
            SummaryDateLocal = SelectedDate;

            var startUtc = TimeZoneInfo.ConvertTimeToUtc(SelectedDate, tz);
            var endUtc = TimeZoneInfo.ConvertTimeToUtc(SelectedDate.AddDays(1), tz);

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

            Inspectors = await _db.Inspectors
                .Where(i => i.Enabled)
                .OrderBy(i => i.DisplayName)
                .ToListAsync();

            var inspectorsByEmail = Inspectors
                .Where(i => !string.IsNullOrWhiteSpace(i.Email))
                .GroupBy(i => i.Email, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().DisplayName, StringComparer.OrdinalIgnoreCase);

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
                        RouteOrder = b.RouteOrder,
                        TimePreference = b.TimePreference,
                        ProjectNumber = b.ProjectNumber,
                        ProjectAddress = b.ProjectAddress ?? string.Empty,
                        ContactName = b.ContactName,
                        ContactPhone = b.ContactPhone,
                        Status = b.Status,
                        AssignedTo = ResolveAssignedToDisplay(b.AssignedTo, inspectorsByEmail),
                        AssignedToValue = b.AssignedTo,
                        Comments = b.Comments ?? string.Empty
                    };
                })
                .ToList();
        }

        // ---------------------------------------------------
        // FULL SUMMARY EMAIL
        // ---------------------------------------------------
        public async Task<IActionResult> OnPostEmailAsync()
        {
            await OnGetAsync();

            var fromMailbox = _notificationOptions.FromMailbox;
            var toEmail = _notificationOptions.FromMailbox;
            var subject = $"Kor Field Reviews - {SummaryDateLocal:yyyy-MM-dd} (Tomorrow)";
            var html = BuildEmailHtml(SummaryDateLocal, Bookings, routeUrl: null);

            if (!await TrySendSummaryEmailAsync(fromMailbox, toEmail, subject, html, "full summary"))
                return RedirectToPage();

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

            var inspectorBookings = inspector is null
                ? new List<SummaryRow>()
                : GetOrderedInspectorBookings(inspector.Email).ToList();

            if (!inspectorBookings.Any())
            {
                StatusMessage = "No bookings found for that inspector.";
                return RedirectToPage();
            }

            var fromMailbox = _notificationOptions.FromMailbox;
            var subject = $"Your Field Reviews - {SummaryDateLocal:yyyy-MM-dd}";
            var html = BuildEmailHtml(
                SummaryDateLocal,
                inspectorBookings,
                BuildGoogleMapsRouteUrl(inspectorBookings),
                preserveRowOrder: true);

            if (!await TrySendSummaryEmailAsync(fromMailbox, inspectorEmail, subject, html, $"inspector summary for {inspectorEmail}"))
                return RedirectToPage();

            StatusMessage = $"Inspector summary sent to {inspectorEmail}.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostEmailAllInspectorsAsync()
        {
            await OnGetAsync();

            var fromMailbox = _notificationOptions.FromMailbox;
            var sentEmails = new List<string>();
            var failedEmails = new List<string>();

            var inspectorsByName = Inspectors
                .Where(i => !string.IsNullOrWhiteSpace(i.DisplayName) && !string.IsNullOrWhiteSpace(i.Email))
                .GroupBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Email, StringComparer.OrdinalIgnoreCase);

            var groups = Bookings
                .Where(b =>
                    !string.IsNullOrWhiteSpace(b.AssignedToValue) &&
                    !string.Equals(b.AssignedTo, "Unassigned", StringComparison.OrdinalIgnoreCase))
                .GroupBy(b => b.AssignedToValue!, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var group in groups)
            {
                var inspectorEmail = group.Key ?? string.Empty;
                if (!inspectorsByName.Values.Contains(inspectorEmail, StringComparer.OrdinalIgnoreCase))
                    continue;

                var inspectorBookings = OrderForRoute(group.ToList()).ToList();
                if (inspectorBookings.Count == 0)
                    continue;

                var subject = $"Your Field Reviews - {SummaryDateLocal:yyyy-MM-dd}";
                var html = BuildEmailHtml(
                    SummaryDateLocal,
                    inspectorBookings,
                    BuildGoogleMapsRouteUrl(inspectorBookings),
                    preserveRowOrder: true);

                try
                {
                    await _mail.SendHtmlAsync(fromMailbox, inspectorEmail, subject, html);
                    sentEmails.Add(inspectorEmail);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to send inspector summary for {SummaryDate} to {InspectorEmail}.",
                        SummaryDateLocal,
                        inspectorEmail);
                    failedEmails.Add(inspectorEmail);
                }
            }

            if (sentEmails.Count == 0 && failedEmails.Count == 0)
            {
                StatusMessage = "No assigned inspector bookings found to email.";
            }
            else if (failedEmails.Count == 0)
            {
                StatusMessage = $"Inspector summaries sent to {sentEmails.Count} inspector(s).";
            }
            else
            {
                StatusMessage =
                    $"Inspector summaries sent: {string.Join(", ", sentEmails)}. " +
                    $"Failed: {string.Join(", ", failedEmails)}.";
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostSaveRouteOrderAsync(RouteOrderRequest request)
        {
            await OnGetAsync();

            var persisted = await PersistRouteOrderAsync(request);
            if (persisted.Count == 0)
            {
                StatusMessage = "No valid route order was provided.";
                return RedirectToPage(new { date = Date });
            }

            StatusMessage = "Route order saved.";
            return RedirectToPage(new { date = Date });
        }

        public async Task<IActionResult> OnPostEmailRouteAsync(RouteOrderRequest request)
        {
            await OnGetAsync();

            var persisted = await PersistRouteOrderAsync(request);
            if (persisted.Count == 0 || string.IsNullOrWhiteSpace(request.InspectorEmail))
            {
                StatusMessage = "No valid route order was provided.";
                return RedirectToPage(new { date = Date });
            }

            var inspector = await _db.Inspectors
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.Email == request.InspectorEmail && i.Enabled);

            if (inspector == null)
            {
                StatusMessage = "Inspector not found.";
                return RedirectToPage(new { date = Date });
            }

            var persistedSet = persisted.ToHashSet();
            var inspectorBookings = OrderForRoute(
                    Bookings.Where(b =>
                        string.Equals(b.AssignedToValue, inspector.Email, StringComparison.OrdinalIgnoreCase) &&
                        persistedSet.Contains(b.BookingId))
                    .ToList())
                .ToList();

            if (inspectorBookings.Count == 0)
            {
                StatusMessage = "No bookings found for that inspector.";
                return RedirectToPage(new { date = Date });
            }

            var html = BuildEmailHtml(
                SummaryDateLocal,
                inspectorBookings,
                BuildGoogleMapsRouteUrl(inspectorBookings),
                preserveRowOrder: true);

            if (!await TrySendSummaryEmailAsync(
                    _notificationOptions.FromMailbox,
                    inspector.Email,
                    $"Your Field Reviews - {SummaryDateLocal:yyyy-MM-dd}",
                    html,
                    $"inspector summary for {inspector.Email}"))
            {
                return RedirectToPage(new { date = Date });
            }

            StatusMessage = $"Inspector summary sent to {inspector.Email}.";
            return RedirectToPage(new { date = Date });
        }

        private async Task<bool> TrySendSummaryEmailAsync(
            string fromMailbox,
            string toEmail,
            string subject,
            string html,
            string operationName)
        {
            try
            {
                await _mail.SendHtmlAsync(fromMailbox, toEmail, subject, html);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to send {OperationName} email for {SummaryDate} to {Recipient}.",
                    operationName,
                    SummaryDateLocal,
                    toEmail);
                StatusMessage = $"Unable to send the {operationName}. Please try again.";
                return false;
            }
        }

        private static string BuildEmailHtml(DateTime dateLocal, IList<SummaryRow> rows, string? routeUrl, bool preserveRowOrder = false)
        {
            var sb = new StringBuilder();

            sb.Append("<h2>Field Reviews for ")
              .Append(WebUtility.HtmlEncode(dateLocal.ToString("yyyy-MM-dd")))
              .Append("</h2>");

            if (!string.IsNullOrWhiteSpace(routeUrl))
            {
                sb.Append("<p><a href=\"")
                  .Append(WebUtility.HtmlEncode(routeUrl))
                  .Append("\">Open inspector route in Google Maps</a></p>");
            }

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

            var orderedRows = preserveRowOrder ? rows : rows.OrderBy(r => r.StartLocal).ToList();

            foreach (var b in orderedRows)
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

        private static string? BuildGoogleMapsRouteUrl(IList<SummaryRow> rows)
        {
            var addresses = new List<string>();

            foreach (var row in rows)
            {
                var address = row.ProjectAddress?.Trim();
                if (string.IsNullOrWhiteSpace(address))
                    continue;

                if (!addresses.Contains(address, StringComparer.OrdinalIgnoreCase))
                    addresses.Add(address);
            }

            if (addresses.Count == 0)
                return null;

            var origin = Uri.EscapeDataString(addresses[0]!);
            var destination = Uri.EscapeDataString(addresses[^1]!);
            var waypoints = addresses.Skip(1).SkipLast(1).Select(Uri.EscapeDataString).ToList();

            var routeUrl = new StringBuilder("https://www.google.com/maps/dir/?api=1")
                .Append("&origin=").Append(origin)
                .Append("&destination=").Append(destination);

            if (waypoints.Count > 0)
                routeUrl.Append("&waypoints=").Append(string.Join("|", waypoints));

            return routeUrl.ToString();
        }

        private IEnumerable<SummaryRow> GetOrderedInspectorBookings(string inspectorEmail)
        {
            return OrderForRoute(
                Bookings.Where(b =>
                        string.Equals(b.AssignedToValue, inspectorEmail, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(b.AssignedTo, "Unassigned", StringComparison.OrdinalIgnoreCase))
                    .ToList());
        }

        private static IEnumerable<SummaryRow> OrderForRoute(IEnumerable<SummaryRow> rows)
        {
            return rows
                .OrderBy(r => r.RouteOrder ?? int.MaxValue)
                .ThenBy(r => r.StartLocal)
                .ThenBy(r => r.ProjectAddress, StringComparer.OrdinalIgnoreCase);
        }

        private async Task<IReadOnlyList<Guid>> PersistRouteOrderAsync(RouteOrderRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.InspectorEmail))
                return Array.Empty<Guid>();

            var orderedIds = ParseOrderedBookingIds(request.OrderedBookingIds);
            if (orderedIds.Count == 0)
                return Array.Empty<Guid>();

            var startUtc = TimeZoneInfo.ConvertTimeToUtc(SelectedDate, _timeRules.TimeZone);
            var endUtc = TimeZoneInfo.ConvertTimeToUtc(SelectedDate.AddDays(1), _timeRules.TimeZone);

            var bookings = await _db.Bookings
                .Where(b =>
                    b.StartUtc >= startUtc &&
                    b.StartUtc < endUtc &&
                    b.Status != "Cancelled" &&
                    b.AssignedTo == request.InspectorEmail &&
                    orderedIds.Contains(b.BookingId))
                .ToListAsync();

            if (bookings.Count == 0)
                return Array.Empty<Guid>();

            var positions = orderedIds
                .Select((id, index) => new { id, index })
                .ToDictionary(x => x.id, x => x.index);

            foreach (var booking in bookings)
            {
                booking.RouteOrder = positions[booking.BookingId];
            }

            await _db.SaveChangesAsync();
            return bookings.Select(b => b.BookingId).ToList();
        }

        private static List<Guid> ParseOrderedBookingIds(string? orderedBookingIds)
        {
            return (orderedBookingIds ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => Guid.TryParse(value, out var parsed) ? parsed : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
        }

        public string ToggleDir(string column)
        {
            if (Sort?.Equals(column, StringComparison.OrdinalIgnoreCase) == true
                && Dir == "asc")
                return "desc";

            return "asc";
        }

        private static string ResolveAssignedToDisplay(
            string? assignedTo,
            IReadOnlyDictionary<string, string> inspectorsByEmail)
        {
            if (string.IsNullOrWhiteSpace(assignedTo))
                return "Unassigned";

            return inspectorsByEmail.TryGetValue(assignedTo, out var displayName)
                ? displayName
                : assignedTo;
        }
    }
}



