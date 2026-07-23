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
        private readonly int _maxBookingsPerSlot;

        public TimeRuleService(IOptions<InspectionRulesOptions> options)
        {
            _options = options.Value;
            var maxBookingsPerSlot = options.Value.MaxBookingsPerSlot;
            _maxBookingsPerSlot = Math.Max(1, maxBookingsPerSlot);
            _tz = TimeZoneInfo.FindSystemTimeZoneById(_options.TimeZoneId);
        }

        public TimeZoneInfo TimeZone => _tz;
        public int DefaultDurationMinutes => _options.DefaultDurationMinutes;

        public async Task<List<Booking>> GetExistingBookingsForLocalDateAsync(
            InspectionsContext db,
            DateOnly localDate)
        {
            var localStart = localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            var localEnd = localDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

            var utcStart = TimeZoneInfo.ConvertTimeToUtc(localStart, _tz);
            var utcEnd = TimeZoneInfo.ConvertTimeToUtc(localEnd, _tz);

            return await db.Bookings
                .Where(b => b.Status != BookingStatus.Cancelled)
                .Where(b => b.StartUtc >= utcStart && b.StartUtc < utcEnd)
                .ToListAsync();
        }

        // --------------------------------------------------
        // Allowed Booking Window
        // --------------------------------------------------

        public (DateOnly MinDate, DateOnly MaxDate) GetAllowedDateRangeUtcNow()
        {
            var nowUtc = DateTime.UtcNow;
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, _tz);

            var cutoff = new TimeOnly(_options.CutoffHourLocal, 0);
            var today = DateOnly.FromDateTime(nowLocal.Date);

            // Walk forward by BUSINESS days, not calendar days. Calendar-day
            // arithmetic with a single trailing weekend-skip diverges from the
            // intended rule on Friday afternoon: Fri+2 calendar days = Sun,
            // skip-once = Mon, but the symmetric reading of "after cutoff you
            // can't book the next business day" requires Tue (Mon is the next
            // business day; cutoff blocks it). IsCancellationAllowed already
            // walks backward in business-day steps, so doing the forward walk
            // in business days too keeps both rules symmetric.
            var businessDaysToAdd = TimeOnly.FromDateTime(nowLocal) < cutoff ? 1 : 2;
            var minDate = today;
            for (int i = 0; i < businessDaysToAdd; i++)
            {
                minDate = minDate.AddDays(1);
                while (minDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                    minDate = minDate.AddDays(1);
            }

            var maxDate = today.AddDays(_options.BookingWindowDays);

            return (minDate, maxDate);
        }

        // --------------------------------------------------
        // Available Slots
        // --------------------------------------------------

        public IEnumerable<TimeOnly> GetAvailableSlotsForDate(
            DateOnly date,
            IEnumerable<Booking> existingBookingsUtc,
            DateOnly? minDateOverride = null)
        {
            var (defaultMinDate, maxDate) = GetAllowedDateRangeUtcNow();
            var effectiveMinDate = minDateOverride ?? defaultMinDate;

            if (date < effectiveMinDate || date > maxDate)
                return Enumerable.Empty<TimeOnly>();

            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
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
                .Where(b => b.Status != BookingStatus.Cancelled)
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

                if (overlapCount < _maxBookingsPerSlot)
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

    }
}
