using Kor.Inspections.App.Data;
using Kor.Inspections.App.Data.Models;
using Kor.Inspections.App.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Kor.Inspections.App.Services
{
    public class TimeRuleService
    {
        private readonly InspectionRulesOptions _options;
        private readonly TimeZoneInfo _tz;

        public TimeRuleService(IOptions<InspectionRulesOptions> options)
        {
            _options = options.Value;
            _options.MaxBookingsPerSlot = Math.Max(1, _options.MaxBookingsPerSlot);
            _tz = TimeZoneInfo.FindSystemTimeZoneById(_options.TimeZoneId);
        }

        public TimeZoneInfo TimeZone => _tz;

        // --------------------------------------------------
        // Allowed Booking Window
        // --------------------------------------------------

        public (DateOnly MinDate, DateOnly MaxDate) GetAllowedDateRangeUtcNow()
        {
            var nowUtc = DateTime.UtcNow;
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, _tz);

            var cutoff = new TimeOnly(_options.CutoffHourLocal, 0);
            var today = DateOnly.FromDateTime(nowLocal.Date);

            DateOnly minDate;

            if (TimeOnly.FromDateTime(nowLocal) < cutoff)
                minDate = today.AddDays(1);
            else
                minDate = today.AddDays(2);

            var maxDate = today.AddDays(_options.BookingWindowDays);

            return (minDate, maxDate);
        }

        // --------------------------------------------------
        // Available Slots
        // --------------------------------------------------

        public IEnumerable<TimeOnly> GetAvailableSlotsForDate(
            DateOnly date,
            IEnumerable<Booking> existingBookingsUtc)
        {
            var (minDate, maxDate) = GetAllowedDateRangeUtcNow();

            if (date < minDate || date > maxDate)
                return Enumerable.Empty<TimeOnly>();

            var workStart = TimeOnly.ParseExact(
                _options.WorkStart, "HH:mm", CultureInfo.InvariantCulture);

            var workEnd = TimeOnly.ParseExact(
                _options.WorkEnd, "HH:mm", CultureInfo.InvariantCulture);

            var slotMinutes = _options.SlotMinutes;

            var dateTimeLocal = date.ToDateTime(workStart);
            var endOfDayLocal = date.ToDateTime(workEnd);

            var duration = TimeSpan.FromMinutes(_options.DefaultDurationMinutes);
            var padding = TimeSpan.FromMinutes(_options.TravelPaddingMinutes);

            var bookingsLocal = existingBookingsUtc
                .Where(b => b.Status != "Cancelled")
                .Select(b => new
                {
                    StartLocal = TimeZoneInfo.ConvertTimeFromUtc(b.StartUtc, _tz),
                    EndLocal = TimeZoneInfo.ConvertTimeFromUtc(b.EndUtc, _tz)
                })
                .ToList();

            var slotList = new List<TimeOnly>();

            while (dateTimeLocal < endOfDayLocal)
            {
                var slotStartLocal = dateTimeLocal;
                var slotEndLocal = slotStartLocal.Add(duration);

                if (slotEndLocal > endOfDayLocal)
                    break;

                var checkStart = slotStartLocal - padding;
                var checkEnd = slotEndLocal + padding;

                var overlapCount = bookingsLocal.Count(b =>
                    b.StartLocal < checkEnd && b.EndLocal > checkStart);

                if (overlapCount < _options.MaxBookingsPerSlot)
                    slotList.Add(TimeOnly.FromDateTime(slotStartLocal));

                dateTimeLocal = dateTimeLocal.AddMinutes(slotMinutes);
            }

            return slotList;
        }

        // --------------------------------------------------
        // Convert Local → UTC
        // --------------------------------------------------

        public DateTime ConvertLocalToUtc(DateOnly date, TimeOnly time)
        {
            var local = date.ToDateTime(time, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(local, _tz);
        }

        public bool IsCancellationAllowed(DateTime bookingStartUtc)
        {
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _tz);
            var bookingLocal = TimeZoneInfo.ConvertTimeFromUtc(bookingStartUtc, _tz);

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
                _options.CutoffHourLocal, 0, 0,
                DateTimeKind.Unspecified);

            return nowLocal <= cutoffLocal;
        }

        // --------------------------------------------------
        // FULLY BOOKED DAYS (for Flatpickr)
        // --------------------------------------------------

        public async Task<List<string>> GetFullyBookedDatesAsync(InspectionsContext db)
        {
            var (min, max) = GetAllowedDateRangeUtcNow();

            // ⭐ Pull bookings ONCE (huge performance win)
            var minStartUtc = TimeZoneInfo.ConvertTimeToUtc(
                min.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified),
                _tz);
            var maxEndUtc = TimeZoneInfo.ConvertTimeToUtc(
                max.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified),
                _tz);

            var bookings = await db.Bookings
                .Where(b => b.Status != "Cancelled")
                .Where(b => b.StartUtc >= minStartUtc && b.StartUtc < maxEndUtc)
                .ToListAsync();

            var results = new List<string>();

            for (var date = min; date <= max; date = date.AddDays(1))
            {
                var localForDate = bookings
                    .Where(b =>
                    {
                        var local = TimeZoneInfo.ConvertTimeFromUtc(b.StartUtc, _tz);
                        return DateOnly.FromDateTime(local) == date;
                    })
                    .ToList();

                var slots = GetAvailableSlotsForDate(date, localForDate);

                if (!slots.Any())
                    results.Add(date.ToString("yyyy-MM-dd"));
            }

            return results;
        }
    }
}
