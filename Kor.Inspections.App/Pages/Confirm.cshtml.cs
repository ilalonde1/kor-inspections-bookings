using System;
using System.Threading.Tasks;
using Kor.Inspections.App.Data.Models;
using Kor.Inspections.App.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Kor.Inspections.App.Options;

namespace Kor.Inspections.App.Pages
{
    public class ConfirmModel : PageModel
    {
        private readonly BookingService _bookingService;
        private readonly TimeRuleService _timeRules;
        private readonly AppOptions _appOptions;
        private readonly SupportOptions _supportOptions;

        public ConfirmModel(
            BookingService bookingService,
            TimeRuleService timeRules,
            IOptions<AppOptions> appOptions,
            IOptions<SupportOptions> supportOptions)
        {
            _bookingService = bookingService;
            _timeRules = timeRules;
            _appOptions = appOptions.Value;
            _supportOptions = supportOptions.Value;
        }

        public Booking? Booking { get; private set; }

        // Exposed to the view
        public string CancelUrl { get; private set; } = string.Empty;
        public TimeZoneInfo TimeZone => _timeRules.TimeZone;
        public string? TimePreference { get; private set; }
        public SupportOptions Support { get; private set; } = new();
        public async Task<IActionResult> OnGetAsync(Guid id)
        {
            ViewData["Title"] = "Booking Confirmed";
            Booking = await _bookingService.GetBookingAsync(id);
            if (Booking == null)
            {
                return NotFound();
            }

            TimePreference = Booking.TimePreference;
            Support = _supportOptions;


            // Prefer configured public base URL if available (works behind proxies / load balancers)
            var baseUrl = (_appOptions.PublicBaseUrl ?? string.Empty).TrimEnd('/');

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                // Fallback to request-based URL
                var scheme = Request.Scheme;
                var host = Request.Host.Value;
                baseUrl = $"{scheme}://{host}";
            }

            CancelUrl = $"{baseUrl}/Manage?token={Booking.CancelToken}";

            return Page();
        }
    }
}
