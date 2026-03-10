using System;
using System.Threading.Tasks;
using Kor.Inspections.App.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Kor.Inspections.App.Services;

namespace Kor.Inspections.App.Pages
{
    public class ManageModel : PageModel
    {
        private readonly InspectionsContext _db;
        private readonly TimeRuleService _timeRules;
        private readonly BookingService _bookingService;

        public ManageModel(
            InspectionsContext db,
            TimeRuleService timeRules,
            BookingService bookingService)
        {
            _db = db;
            _timeRules = timeRules;
            _bookingService = bookingService;
        }

        [BindProperty(SupportsGet = true)]
        public Guid Token { get; set; }

        // View state
        public bool BookingNotFound { get; private set; }
        public bool AlreadyCancelled { get; private set; }
        public bool CancelledSuccessfully { get; private set; }
        public bool CanCancel { get; private set; }

        // Display fields
        public string ProjectNumber { get; private set; } = "";
        public string LocalDateText { get; private set; } = "";
        public string LocalTimeText { get; private set; } = "";
        public string StatusText { get; private set; } = "";
        public string? AssignedTo { get; private set; }

        public async Task OnGetAsync()
        {
            await LoadAsync();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var booking = await _db.Bookings.SingleOrDefaultAsync(b => b.CancelToken == Token);

            if (booking == null)
            {
                BookingNotFound = true;
                return Page();
            }

            if (string.Equals(booking.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                AlreadyCancelled = true;
                await LoadAsync();
                return Page();
            }

            if (!IsCancellationAllowed(booking.StartUtc))
            {
                await LoadAsync();
                return Page();
            }

            booking.Status = "Cancelled";
            await _db.SaveChangesAsync();

            // SEND CANCELLATION EMAILS
            await _bookingService.SendCancellationEmailsAsync(booking);

            CancelledSuccessfully = true;
            await LoadAsync();
            return Page();
        }

        private async Task LoadAsync()
        {
            if (Token == Guid.Empty)
            {
                BookingNotFound = true;
                return;
            }

            var booking = await _db.Bookings.SingleOrDefaultAsync(b => b.CancelToken == Token);

            if (booking == null)
            {
                BookingNotFound = true;
                return;
            }

            AlreadyCancelled = string.Equals(
                booking.Status, "Cancelled", StringComparison.OrdinalIgnoreCase);

            var tz = _timeRules.TimeZone;

            var localStart = TimeZoneInfo.ConvertTimeFromUtc(booking.StartUtc, tz);
            var localEnd = TimeZoneInfo.ConvertTimeFromUtc(booking.EndUtc, tz); // ADD THIS

            ProjectNumber = booking.ProjectNumber;
            LocalDateText = localStart.ToString("yyyy-MM-dd (ddd)");

            LocalTimeText = BookingDisplayHelper.GetTimeDisplay(
                booking.TimePreference,
                localStart,
                localEnd);

            StatusText = booking.Status;
            AssignedTo = booking.AssignedTo;


            CanCancel = !AlreadyCancelled && IsCancellationAllowed(booking.StartUtc);
        }

        /// <summary>
        /// Cancellation allowed until 2:00 PM Pacific on the previous business day.
        /// Business days are Mon–Fri (holidays not included yet).
        /// </summary>
        private bool IsCancellationAllowed(DateTime bookingStartUtc)
        {
            var tz = _timeRules.TimeZone;

            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            var bookingLocal = TimeZoneInfo.ConvertTimeFromUtc(bookingStartUtc, tz);

            if (bookingLocal <= nowLocal)
                return false;

            var bookingDate = bookingLocal.Date;

            var cutoffDay = bookingDate.AddDays(-1);
            while (cutoffDay.DayOfWeek == DayOfWeek.Saturday ||
                   cutoffDay.DayOfWeek == DayOfWeek.Sunday)
            {
                cutoffDay = cutoffDay.AddDays(-1);
            }

            var cutoffLocal = new DateTime(
                cutoffDay.Year, cutoffDay.Month, cutoffDay.Day,
                14, 0, 0,
                DateTimeKind.Unspecified);

            return nowLocal <= cutoffLocal;
        }
    }
}
