using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
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
        public List<string> AvailableManualTimes { get; private set; } = new();

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

        [BindProperty]
        public ManualBookingInput ManualBooking { get; set; } = new();

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

        public sealed class ManualBookingInput
        {
            [Required(ErrorMessage = "Kor Job Number is required.")]
            [RegularExpression(@"^\s*\d{5}.*$", ErrorMessage = "Job number must start with 5 digits (e.g., 30844-01).")]
            [Display(Name = "Kor Job Number")]
            public string ProjectNumber { get; set; } = string.Empty;

            [Required(ErrorMessage = "Project address is required.")]
            [StringLength(255)]
            [Display(Name = "Project Address")]
            public string ProjectAddress { get; set; } = string.Empty;

            [Required(ErrorMessage = "Site contact name is required.")]
            [StringLength(80)]
            [Display(Name = "Site Contact Name")]
            public string ContactName { get; set; } = string.Empty;

            [Required(ErrorMessage = "Site contact phone is required.")]
            [RegularExpression(
                @"^\s*(\+?1[\s\-\.]?)?(\(?\d{3}\)?[\s\-\.]?)\d{3}[\s\-\.]?\d{4}\s*$",
                ErrorMessage = "Enter a valid 10-digit phone number.")]
            [StringLength(16)]
            [Display(Name = "Site Contact Phone")]
            public string ContactPhone { get; set; } = string.Empty;

            [Required(ErrorMessage = "Contact email is required.")]
            [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
            [StringLength(120)]
            [Display(Name = "Contact Email")]
            public string ContactEmail { get; set; } = string.Empty;

            [Required(ErrorMessage = "Inspection details are required.")]
            [StringLength(500, MinimumLength = 5, ErrorMessage = "Please provide a bit more detail (at least 5 characters).")]
            [Display(Name = "Inspection Details")]
            public string? Comments { get; set; }

            [Required(ErrorMessage = "Requested date is required.")]
            [Display(Name = "Requested Date")]
            public DateTime? RequestedDate { get; set; }

            [Required(ErrorMessage = "Requested time is required.")]
            [RegularExpression(@"^\d{2}:\d{2}$", ErrorMessage = "Requested time must use HH:mm.")]
            [Display(Name = "Requested Time")]
            public string RequestedTime { get; set; } = string.Empty;

            [Display(Name = "Override the 2:00 p.m. next-day cutoff")]
            public bool OverrideCutoff { get; set; }
        }

        // --------------------------------------------------

        public async Task OnGetAsync()
        {
            ViewData["Title"] = "Admin";
            InitializeManualBookingDefaults();
            await LoadDataAsync();
            await LoadManualBookingTimesAsync();
        }

        public string FormatPhone(string phone) => PhoneNormalizer.Format(phone);

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

        public async Task<IActionResult> OnPostCreateAsync()
        {
            ViewData["Title"] = "Admin";

            if (!ValidateManualBookingInput())
            {
                await LoadDataAsync();
                return Page();
            }

            var requestedDate = DateOnly.FromDateTime(ManualBooking.RequestedDate!.Value);
            var (_, maxDate) = _timeRules.GetAllowedDateRangeUtcNow();
            var minDate = GetMinimumManualBookingDate(ManualBooking.OverrideCutoff);

            if (requestedDate < minDate || requestedDate > maxDate)
            {
                ModelState.AddModelError(
                    "ManualBooking.RequestedDate",
                    ManualBooking.OverrideCutoff
                        ? "Selected date is outside the allowed booking window, even with the cutoff override."
                        : "Selected date is outside the allowed booking window.");
                await LoadDataAsync();
                await LoadManualBookingTimesAsync();
                return Page();
            }

            var existingForDate = await GetExistingBookingsForLocalDateAsync(requestedDate);
            var projectNumber = ProjectNumberHelper.Base5(ManualBooking.ProjectNumber.Trim());
            var submittedContactEmail = ManualBooking.ContactEmail.Trim();
            DateTime startUtc;
            DateTime endUtc;

            if (ManualBooking.RequestedTime == "AM" || ManualBooking.RequestedTime == "PM")
            {
                var anchorTime = ManualBooking.RequestedTime == "AM"
                    ? new TimeOnly(8, 0)
                    : new TimeOnly(12, 0);

                startUtc = _timeRules.ConvertLocalToUtc(requestedDate, anchorTime);
                endUtc = startUtc.AddHours(4);
            }
            else
            {
                if (!TimeOnly.TryParseExact(ManualBooking.RequestedTime, "HH:mm", out var requestedTime))
                {
                    ModelState.AddModelError("ManualBooking.RequestedTime", "Invalid time selected.");
                    await LoadDataAsync();
                    await LoadManualBookingTimesAsync();
                    return Page();
                }

                var availableSlots = _timeRules.GetAvailableSlotsForDate(requestedDate, existingForDate).ToList();

                if (!availableSlots.Contains(requestedTime))
                {
                    ModelState.AddModelError(
                        "ManualBooking.RequestedTime",
                        "Selected time is no longer available. Please choose another time.");
                    await LoadDataAsync();
                    await LoadManualBookingTimesAsync();
                    return Page();
                }

                startUtc = _timeRules.ConvertLocalToUtc(requestedDate, requestedTime);
                endUtc = startUtc.AddMinutes(60);
            }
            var duplicateCutoffUtc = DateTime.UtcNow.AddMinutes(-2);

            var duplicateExists = await _db.Bookings
                .AsNoTracking()
                .AnyAsync(b =>
                    b.ProjectNumber == projectNumber &&
                    b.ContactEmail == submittedContactEmail &&
                    b.StartUtc == startUtc &&
                    b.Status != "Cancelled" &&
                    b.CreatedUtc >= duplicateCutoffUtc);

            if (duplicateExists)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "This booking was already submitted recently. Please check the booking list before trying again.");
                await LoadDataAsync();
                await LoadManualBookingTimesAsync();
                return Page();
            }

            try
            {
                await _bookingService.CreateBookingAsync(
                    projectNumber,
                    ManualBooking.ProjectAddress.Trim(),
                    ManualBooking.ContactName.Trim(),
                    PhoneNormalizer.Normalize(ManualBooking.ContactPhone),
                    submittedContactEmail,
                    string.IsNullOrWhiteSpace(ManualBooking.Comments) ? null : ManualBooking.Comments.Trim(),
                    startUtc,
                    endUtc,
                    ManualBooking.RequestedTime is "AM" or "PM" ? ManualBooking.RequestedTime : null);
            }
            catch (BookingSlotUnavailableException)
            {
                ModelState.AddModelError(
                    "ManualBooking.RequestedTime",
                    "Selected time is no longer available. Please choose another time.");
                await LoadDataAsync();
                await LoadManualBookingTimesAsync();
                return Page();
            }

            _logger.LogInformation(
                "Admin manual booking created: ProjectNumber={ProjectNumber} StartUtc={StartUtc} By={AdminUser} OverrideCutoff={OverrideCutoff}",
                projectNumber,
                startUtc,
                User.Identity?.Name,
                ManualBooking.OverrideCutoff);

            StatusMessage = ManualBooking.OverrideCutoff
                ? "Booking created with cutoff override."
                : "Booking created.";

            return RedirectToPage(new { sort = Sort, dir = Dir, view = View, project = Project, inspector = Inspector, dateFrom = DateFrom, dateTo = DateTo, pageIndex = PageIndex });
        }

        public async Task<IActionResult> OnPostRefreshManualTimesAsync()
        {
            ViewData["Title"] = "Admin";
            InitializeManualBookingDefaults();
            await LoadDataAsync();
            await LoadManualBookingTimesAsync();
            return Page();
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

        private void InitializeManualBookingDefaults()
        {
            if (!ManualBooking.RequestedDate.HasValue)
                ManualBooking.RequestedDate = GetMinimumManualBookingDate(overrideCutoff: false).ToDateTime(TimeOnly.MinValue);

            if (string.IsNullOrWhiteSpace(ManualBooking.RequestedTime))
                ManualBooking.RequestedTime = "AM";
        }

        private bool ValidateManualBookingInput()
        {
            var validationResults = new List<ValidationResult>();
            var validationContext = new ValidationContext(ManualBooking);

            if (Validator.TryValidateObject(ManualBooking, validationContext, validationResults, validateAllProperties: true))
                return true;

            foreach (var result in validationResults)
            {
                var members = result.MemberNames.Any()
                    ? result.MemberNames
                    : new[] { string.Empty };

                foreach (var memberName in members)
                {
                    var key = string.IsNullOrWhiteSpace(memberName)
                        ? string.Empty
                        : $"ManualBooking.{memberName}";
                    ModelState.AddModelError(key, result.ErrorMessage ?? "Invalid booking input.");
                }
            }

            return false;
        }

        private DateOnly GetMinimumManualBookingDate(bool overrideCutoff)
        {
            var (minDate, _) = _timeRules.GetAllowedDateRangeUtcNow();
            if (!overrideCutoff)
                return minDate;

            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeRules.TimeZone);
            var tomorrow = DateOnly.FromDateTime(nowLocal.Date.AddDays(1));
            return tomorrow < minDate ? tomorrow : minDate;
        }

        private async Task<List<Booking>> GetExistingBookingsForLocalDateAsync(DateOnly localDate)
        {
            var localStart = localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            var localEnd = localDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

            var utcStart = TimeZoneInfo.ConvertTimeToUtc(localStart, _timeRules.TimeZone);
            var utcEnd = TimeZoneInfo.ConvertTimeToUtc(localEnd, _timeRules.TimeZone);

            return await _db.Bookings
                .Where(b => b.Status != "Cancelled")
                .Where(b => b.StartUtc >= utcStart && b.StartUtc < utcEnd)
                .ToListAsync();
        }

        private async Task LoadManualBookingTimesAsync()
        {
            AvailableManualTimes = new();

            if (!ManualBooking.RequestedDate.HasValue)
                return;

            var requestedDate = DateOnly.FromDateTime(ManualBooking.RequestedDate.Value);
            var existingForDate = await GetExistingBookingsForLocalDateAsync(requestedDate);
            AvailableManualTimes = _timeRules
                .GetAvailableSlotsForDate(requestedDate, existingForDate)
                .Select(t => t.ToString("HH:mm"))
                .ToList();

            if (ManualBooking.RequestedTime is not "AM" and not "PM" &&
                !string.IsNullOrWhiteSpace(ManualBooking.RequestedTime) &&
                !AvailableManualTimes.Contains(ManualBooking.RequestedTime, StringComparer.Ordinal))
            {
                ManualBooking.RequestedTime = string.Empty;
            }
        }
    }
}
