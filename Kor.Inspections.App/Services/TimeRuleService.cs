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
        private readonly TimeProvider _clock;
        private readonly Dictionary<int, HashSet<DateOnly>> _closedWeekdaysByYear = new();

        public TimeRuleService(IOptions<InspectionRulesOptions> options, TimeProvider? clock = null)
        {
            _options = options.Value;
            _clock = clock ?? TimeProvider.System;
            var maxBookingsPerSlot = options.Value.MaxBookingsPerSlot;
            _maxBookingsPerSlot = Math.Max(1, maxBookingsPerSlot);
            _tz = TimeZoneInfo.FindSystemTimeZoneById(_options.TimeZoneId);
        }

        public TimeZoneInfo TimeZone => _tz;
        public int DefaultDurationMinutes => _options.DefaultDurationMinutes;

        /// <summary>
        /// Mon-Fri, excluding BC statutory holidays and configured closures
        /// (with weekend holidays observed on the next weekday).
        /// </summary>
        public bool IsBusinessDay(DateOnly date)
        {
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                return false;

            if (!_closedWeekdaysByYear.TryGetValue(date.Year, out var closed))
            {
                closed = BcHolidayCalendar.GetClosedWeekdays(date.Year, _options.ClosedDates);
                _closedWeekdaysByYear[date.Year] = closed;
            }

            return !closed.Contains(date);
        }

        /// <summary>Non-business days in [from, to], for disabling them in the date picker.</summary>
        public List<DateOnly> GetNonBusinessDays(DateOnly from, DateOnly to)
        {
            var days = new List<DateOnly>();
            for (var d = from; d <= to; d = d.AddDays(1))
            {
                if (!IsBusinessDay(d))
                    days.Add(d);
            }
            return days;
        }

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
            var nowUtc = _clock.GetUtcNow().UtcDateTime;
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
            //
            // Weekends and BC stat holidays are non-business days. Booking on one
            // counts as past the last business day's cutoff: on a weekend before
            // a normal Monday the earliest date is Tuesday; before a holiday
            // Monday it is Wednesday.
            var pastCutoff = !IsBusinessDay(today) || TimeOnly.FromDateTime(nowLocal) >= cutoff;
            var businessDaysToAdd = pastCutoff ? 2 : 1;
            var minDate = today;
            for (int i = 0; i < businessDaysToAdd; i++)
            {
                minDate = minDate.AddDays(1);
                while (!IsBusinessDay(minDate))
                    minDate = minDate.AddDays(1);
            }

            var maxDate = today.AddDays(_options.BookingWindowDays);

            return (minDate, maxDate);
        }

        /// <summary>
        /// Earliest date for an admin-only booking on a non-business day (weekend
        /// or holiday; never offered publicly). Uses the calendar-day form of the
        /// cutoff - before CutoffHourLocal the next day is open, after it the day
        /// after - so staff can book this Saturday on Friday morning without an
        /// override.
        /// </summary>
        public DateOnly GetNonBusinessDayMinDateUtcNow()
        {
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(_clock.GetUtcNow().UtcDateTime, _tz);
            var today = DateOnly.FromDateTime(nowLocal.Date);
            var cutoff = new TimeOnly(_options.CutoffHourLocal, 0);
            return today.AddDays(TimeOnly.FromDateTime(nowLocal) < cutoff ? 1 : 2);
        }

        // --------------------------------------------------
        // Available Slots
        // --------------------------------------------------

        public IEnumerable<TimeOnly> GetAvailableSlotsForDate(
            DateOnly date,
            IEnumerable<Booking> existingBookingsUtc,
            DateOnly? minDateOverride = null,
            bool allowNonBusinessDays = false)
        {
            var (defaultMinDate, maxDate) = GetAllowedDateRangeUtcNow();
            var effectiveMinDate = minDateOverride ?? defaultMinDate;

            if (date < effectiveMinDate || date > maxDate)
                return Enumerable.Empty<TimeOnly>();

            if (!allowNonBusinessDays && !IsBusinessDay(date))
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
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(_clock.GetUtcNow().UtcDateTime, _tz);
            var bookingLocal = TimeZoneInfo.ConvertTimeFromUtc(bookingStartUtc, _tz);

            if (bookingLocal <= nowLocal)
                return false;

            var bookingDate = bookingLocal.Date;

            // Notice is due by the cutoff on the previous business day.
            var cutoffDay = bookingDate.AddDays(-1);
            while (!IsBusinessDay(DateOnly.FromDateTime(cutoffDay)))
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
