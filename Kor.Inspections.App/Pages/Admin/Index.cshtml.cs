using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kor.Inspections.App.Data;
using Kor.Inspections.App.Data.Models;
using Kor.Inspections.App.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kor.Inspections.App.Pages.Admin
{
    public class IndexModel : PageModel
    {
        private const int MaxQueryDays = 90;
        private const int PageSize = 50;

        private readonly InspectionsContext _db;
        private readonly TimeRuleService _timeRules;
        private readonly BookingService _bookingService;
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(
            InspectionsContext db,
            TimeRuleService timeRules,
            BookingService bookingService,
            ILogger<IndexModel> logger)
        {
            _db = db;
            _timeRules = timeRules;
            _bookingService = bookingService;
            _logger = logger;
        }

        public IList<BookingRow> Bookings { get; private set; } = new List<BookingRow>();

        public IList<Inspector> Inspectors { get; private set; } = new List<Inspector>();

        [TempData]
        public string? StatusMessage { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Sort { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Dir { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? View { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Project { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Inspector { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? DateFrom { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? DateTo { get; set; }

        [BindProperty(SupportsGet = true)]
        public int PageIndex { get; set; }

        public int TotalCount { get; private set; }

        // --------------------------------------------------
        // VIEW MODEL
        // --------------------------------------------------

        public class BookingRow
        {
            public Guid BookingId { get; set; }

            public DateTime StartLocal { get; set; }
            public DateTime EndLocal { get; set; }

            // CRITICAL � used for Anytime pills
            public string? TimePreference { get; set; }

            public string ProjectNumber { get; set; } = string.Empty;
            public string ProjectAddress { get; set; } = string.Empty;

            public string ContactName { get; set; } = string.Empty;
            public string ContactPhone { get; set; } = string.Empty;
            public string ContactEmail { get; set; } = string.Empty;

            public string Status { get; set; } = string.Empty;
            public string? AssignedToValue { get; set; }
            public string AssignedTo { get; set; } = string.Empty;

            public string? Comments { get; set; }
        }

        // --------------------------------------------------

        public async Task OnGetAsync()
        {
            await LoadDataAsync();
        }

        public IActionResult OnGetMobileActionLog(string evt)
        {
            if (string.IsNullOrWhiteSpace(evt))
                return new JsonResult(new { ok = false });

            if (evt != "mobile_call_clicked" &&
                evt != "mobile_map_clicked" &&
                evt != "mobile_route_clicked" &&
                evt != "desktop_call_clicked" &&
                evt != "desktop_map_clicked" &&
                evt != "desktop_route_clicked")
            {
                return new JsonResult(new { ok = false });
            }

            _logger.LogInformation(
                "AdminAction {Event} User={User}",
                evt,
                User?.Identity?.Name ?? "anonymous");

            return new JsonResult(new { ok = true });
        }

        // --------------------------------------------------
        // ASSIGN
        // --------------------------------------------------

        public async Task<IActionResult> OnPostAssignAsync(Guid id, string assignedTo)
        {
            var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.BookingId == id);
            Inspector? inspector = null;
            var assignedInspectorLabel = "Unassigned";

            if (booking == null)
            {
                StatusMessage = "Booking not found.";
                return RedirectToPage(new { sort = Sort, dir = Dir, view = View, project = Project, inspector = Inspector, dateFrom = DateFrom, dateTo = DateTo, pageIndex = PageIndex });
            }

            if (string.Equals(booking.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(booking.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = "Booking cannot be modified.";
                return RedirectToPage(new { sort = Sort, dir = Dir, view = View, project = Project, inspector = Inspector, dateFrom = DateFrom, dateTo = DateTo, pageIndex = PageIndex });
            }

            var wasUnassigned =
                string.Equals(booking.Status, "Unassigned", StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(assignedTo) &&
                !assignedTo.Equals("Unassigned", StringComparison.OrdinalIgnoreCase))
            {
                var assignedToTrimmed = assignedTo.Trim();
                inspector = await _db.Inspectors
                    .FirstOrDefaultAsync(i =>
                        i.Enabled &&
                        (i.Email == assignedToTrimmed || i.DisplayName == assignedToTrimmed));

                if (inspector == null)
                {
                    StatusMessage = $"Inspector '{assignedTo}' not found or is disabled.";
                    return RedirectToPage(new { sort = Sort, dir = Dir, view = View, project = Project, inspector = Inspector, dateFrom = DateFrom, dateTo = DateTo, pageIndex = PageIndex });
                }
            }

            if (string.IsNullOrWhiteSpace(assignedTo) ||
                assignedTo.Equals("Unassigned", StringComparison.OrdinalIgnoreCase))
            {
                booking.AssignedTo = null;
                booking.Status = "Unassigned";
                StatusMessage = "Booking marked unassigned.";
            }
            else
            {
                booking.AssignedTo = inspector!.Email;
                assignedInspectorLabel = inspector.DisplayName;

                if (booking.Status.Equals("Unassigned", StringComparison.OrdinalIgnoreCase))
                    booking.Status = "Assigned";

                StatusMessage = $"Booking assigned to {assignedInspectorLabel}.";
            }

            _db.BookingActions.Add(new BookingAction
            {
                BookingId = booking.BookingId,
                ActionType = "Assigned",
                PerformedBy = User.Identity?.Name,
                Notes = $"Assigned to {assignedInspectorLabel}",
                ActionUtc = DateTime.UtcNow
            });

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                _db.ChangeTracker.Clear();
                StatusMessage = "This booking was just modified by another user. Please refresh and try again.";
                return RedirectToPage(new { sort = Sort, dir = Dir, view = View, project = Project, inspector = Inspector, dateFrom = DateFrom, dateTo = DateTo, pageIndex = PageIndex });
            }

            _logger.LogInformation(
                "Admin booking assignment: BookingId={BookingId} AssignedTo={AssignedTo} By={AdminUser}",
                booking.BookingId,
                assignedInspectorLabel,
                User.Identity?.Name);

            if (wasUnassigned &&
                booking.Status.Equals("Assigned", StringComparison.OrdinalIgnoreCase))
            {
                await _bookingService.SendAssignmentEmailAsync(booking);
            }

            return RedirectToPage(new { sort = Sort, dir = Dir, view = View, project = Project, inspector = Inspector, dateFrom = DateFrom, dateTo = DateTo, pageIndex = PageIndex });
        }
        // --------------------------------------------------
        // CANCEL
        // --------------------------------------------------

        public async Task<IActionResult> OnPostCancelAsync(Guid id)
        {
            var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.BookingId == id);

            if (booking == null)
            {
                StatusMessage = "Booking not found.";
                return RedirectToPage(new { sort = Sort, dir = Dir, view = View, project = Project, inspector = Inspector, dateFrom = DateFrom, dateTo = DateTo, pageIndex = PageIndex });
            }

            if (string.Equals(booking.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = "Booking cancelled.";
                return RedirectToPage(new { sort = Sort, dir = Dir, view = View, project = Project, inspector = Inspector, dateFrom = DateFrom, dateTo = DateTo, pageIndex = PageIndex });
            }

            booking.Status = "Cancelled";
            _db.BookingActions.Add(new BookingAction
            {
                BookingId = booking.BookingId,
                ActionType = "Cancelled",
                PerformedBy = User.Identity?.Name,
                ActionUtc = DateTime.UtcNow
            });
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                _db.ChangeTracker.Clear();
                StatusMessage = "This booking was just modified by another user. Please refresh and try again.";
                return RedirectToPage(new { sort = Sort, dir = Dir, view = View, project = Project, inspector = Inspector, dateFrom = DateFrom, dateTo = DateTo, pageIndex = PageIndex });
            }

            _logger.LogInformation(
                "Admin booking cancellation: BookingId={BookingId} By={AdminUser}",
                booking.BookingId,
                User.Identity?.Name);

            await _bookingService.SendCancellationEmailsAsync(booking);

            StatusMessage = "Booking cancelled.";
            return RedirectToPage(new { sort = Sort, dir = Dir, view = View, project = Project, inspector = Inspector, dateFrom = DateFrom, dateTo = DateTo, pageIndex = PageIndex });
        }

        // --------------------------------------------------
        // LOAD GRID
        // --------------------------------------------------

        private async Task LoadDataAsync()
        {
            var tz = _timeRules.TimeZone;

            Inspectors = await _db.Inspectors
                .Where(i => i.Enabled)
                .OrderBy(i => i.DisplayName)
                .ToListAsync();

            var inspectorsByEmail = Inspectors
                .Where(i => !string.IsNullOrWhiteSpace(i.Email))
                .GroupBy(i => i.Email, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().DisplayName, StringComparer.OrdinalIgnoreCase);

            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            var defaultWindowStartLocal = nowLocal.Date;
            var windowStartLocal = defaultWindowStartLocal;
            var windowEndLocal = nowLocal.Date.AddDays(8);

            if (DateTime.TryParse(DateFrom, out var dateFromLocal))
            {
                windowStartLocal = dateFromLocal.Date;
            }
            else
            {
                DateFrom = defaultWindowStartLocal.ToString("yyyy-MM-dd");
            }

            if (DateTime.TryParse(DateTo, out var dateToLocal))
            {
                windowEndLocal = dateToLocal.Date.AddDays(1);
            }

            if ((windowEndLocal - windowStartLocal).TotalDays > MaxQueryDays)
            {
                windowEndLocal = windowStartLocal.AddDays(MaxQueryDays);
                DateTo = windowEndLocal.AddDays(-1).ToString("yyyy-MM-dd");
            }

            var startUtc = TimeZoneInfo.ConvertTimeToUtc(windowStartLocal, tz);
            var endUtc = TimeZoneInfo.ConvertTimeToUtc(windowEndLocal, tz);

            var query = _db.Bookings
                .Where(b =>
                    b.StartUtc >= startUtc &&
                    b.StartUtc < endUtc);

            if (!string.Equals(View, "all", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(b => b.Status != "Completed" && b.Status != "Cancelled");
            }

            if (!string.IsNullOrWhiteSpace(Project))
            {
                var projectFilter = Project.Trim();
                query = query.Where(b => b.ProjectNumber.Contains(projectFilter));
            }

            if (!string.IsNullOrWhiteSpace(Inspector))
            {
                var inspectorFilter = Inspector.Trim();
                var matchingInspectors = Inspectors
                    .Where(i =>
                        i.DisplayName.Contains(inspectorFilter, StringComparison.OrdinalIgnoreCase) ||
                        i.Email.Contains(inspectorFilter, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var matchingInspectorEmails = matchingInspectors
                    .Select(i => i.Email)
                    .ToList();

                var matchingInspectorNames = matchingInspectors
                    .Select(i => i.DisplayName)
                    .ToList();

                query = query.Where(b =>
                    (b.AssignedTo ?? "").Contains(inspectorFilter) ||
                    matchingInspectorEmails.Contains(b.AssignedTo ?? "") ||
                    matchingInspectorNames.Contains(b.AssignedTo ?? ""));
            }

            query = (Sort?.ToLower(), Dir?.ToLower()) switch
            {
                ("job", "desc") => query.OrderByDescending(b => b.ProjectNumber),
                ("job", _) => query.OrderBy(b => b.ProjectNumber),

                ("status", "desc") => query.OrderByDescending(b => b.Status),
                ("status", _) => query.OrderBy(b => b.Status),

                ("assigned", "desc") => query.OrderByDescending(b => b.AssignedTo),
                ("assigned", _) => query.OrderBy(b => b.AssignedTo),

                ("date", "desc") => query.OrderByDescending(b => b.StartUtc),
                _ => query.OrderBy(b => b.StartUtc),
            };

            TotalCount = await query.CountAsync();

            if (PageIndex < 0)
                PageIndex = 0;

            var rawBookings = await query
                .Skip(PageIndex * PageSize)
                .Take(PageSize)
                .ToListAsync();

            Bookings = rawBookings.Select(b =>
            {
                var startLocal = TimeZoneInfo.ConvertTimeFromUtc(b.StartUtc, tz);
                var endLocal = TimeZoneInfo.ConvertTimeFromUtc(b.EndUtc, tz);

                return new BookingRow
                {
                    BookingId = b.BookingId,
                    StartLocal = startLocal,
                    EndLocal = endLocal,

                    // ? NORMALIZE HERE � THIS FIXES YOUR UI
                    TimePreference = string.IsNullOrWhiteSpace(b.TimePreference)
                        ? null
                        : b.TimePreference.ToUpper(),

                    ProjectNumber = b.ProjectNumber,
                    ProjectAddress = b.ProjectAddress ?? "",
                    ContactName = b.ContactName,
                    ContactPhone = b.ContactPhone,
                    ContactEmail = b.ContactEmail ?? "",
                    Status = b.Status,
                    AssignedToValue = b.AssignedTo,
                    AssignedTo = ResolveAssignedToDisplay(b.AssignedTo, inspectorsByEmail),
                    Comments = b.Comments
                };
            }).ToList();
        }

        // --------------------------------------------------

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

        public string ToggleDir(string column)
        {
            if (Sort?.Equals(column, StringComparison.OrdinalIgnoreCase) == true
                && Dir == "asc")
                return "desc";

            return "asc";
        }
    }
}
