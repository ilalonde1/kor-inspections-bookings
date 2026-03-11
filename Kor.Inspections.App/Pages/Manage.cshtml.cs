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
        public bool IsTerminalState { get; private set; }

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
            var booking = await _bookingService.GetBookingByCancelTokenAsync(Token);

            if (booking == null)
            {
                BookingNotFound = true;
                return Page();
            }

            if (string.Equals(booking.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                AlreadyCancelled = true;
                await LoadAsync(booking);
                return Page();
            }

            if (string.Equals(booking.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                await LoadAsync(booking);
                return Page();
            }

            if (!_timeRules.IsCancellationAllowed(booking.StartUtc))
            {
                await LoadAsync(booking);
                return Page();
            }

            var cancelled = await _bookingService.CancelBookingByTokenAsync(Token);
            CancelledSuccessfully = cancelled;
            await LoadAsync(booking);
            return Page();
        }

        private async Task LoadAsync(Kor.Inspections.App.Data.Models.Booking? booking = null)
        {
            if (Token == Guid.Empty)
            {
                BookingNotFound = true;
                return;
            }

            booking ??= await _db.Bookings.SingleOrDefaultAsync(b => b.CancelToken == Token);

            if (booking == null)
            {
                BookingNotFound = true;
                return;
            }

            AlreadyCancelled = string.Equals(
                booking.Status, "Cancelled", StringComparison.OrdinalIgnoreCase);

            var isTerminal = (string.Equals(booking.Status, "Completed", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(booking.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)) &&
                             !_timeRules.IsCancellationAllowed(booking.StartUtc);
            if (isTerminal)
            {
                IsTerminalState = true;
                return;
            }

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
            AssignedTo = await ResolveAssignedToDisplayAsync(booking.AssignedTo);


            CanCancel = !AlreadyCancelled && _timeRules.IsCancellationAllowed(booking.StartUtc);
        }

        private async Task<string?> ResolveAssignedToDisplayAsync(string? assignedTo)
        {
            if (string.IsNullOrWhiteSpace(assignedTo))
                return assignedTo;

            var displayName = await _db.Inspectors
                .Where(i => i.Email == assignedTo)
                .Select(i => i.DisplayName)
                .FirstOrDefaultAsync();

            return string.IsNullOrWhiteSpace(displayName) ? assignedTo : displayName;
        }
    }
}
