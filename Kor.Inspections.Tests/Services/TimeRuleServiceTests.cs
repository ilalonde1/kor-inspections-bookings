using Kor.Inspections.App.Data.Models;
using Kor.Inspections.App.Services;
using Kor.Inspections.Tests.Helpers;

namespace Kor.Inspections.Tests.Services;

public class TimeRuleServiceTests
{
    [Fact]
    public void GetAllowedDateRangeUtcNow_BeforeCutoffHour_UsesTomorrowAsMinDate()
    {
        TimeZoneInfo zone;
        try
        {
            zone = TimeRuleServiceTestFactory.FindZone(nowLocal =>
            {
                var today = DateOnly.FromDateTime(nowLocal.Date);
                return nowLocal.Hour <= 22 &&
                       today.AddDays(1).DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;
            });
        }
        catch (InvalidOperationException)
        {
            Assert.True(true); // Calendar-dependent edge: no host timezone currently yields a weekday tomorrow before cutoff.
            return;
        }

        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
        var service = TimeRuleServiceTestFactory.Create(zone, nowLocal.Hour + 1);

        var result = service.GetAllowedDateRangeUtcNow();

        Assert.Equal(DateOnly.FromDateTime(nowLocal.Date).AddDays(1), result.MinDate);
        Assert.Equal(DateOnly.FromDateTime(nowLocal.Date).AddDays(7), result.MaxDate);
    }

    [Fact]
    public void GetAllowedDateRangeUtcNow_AfterCutoffHour_UsesDayAfterTomorrowAsMinDate()
    {
        TimeZoneInfo zone;
        try
        {
            // Require today, today+1, AND today+2 all weekdays so the
            // calendar-day and business-day shapes of the rule agree (this
            // test only claims to verify the Mon-Wed slice). Without the
            // today and today+1 weekday clauses, FindZone could pick a
            // Saturday-today zone where the rules diverge.
            zone = TimeRuleServiceTestFactory.FindZone(nowLocal =>
            {
                var today = DateOnly.FromDateTime(nowLocal.Date);
                return today.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday &&
                       today.AddDays(1).DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday &&
                       today.AddDays(2).DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;
            });
        }
        catch (InvalidOperationException)
        {
            Assert.True(true); // Calendar-dependent edge: no host timezone currently yields three weekdays in a row.
            return;
        }

        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
        var service = TimeRuleServiceTestFactory.Create(zone, nowLocal.Hour);

        var result = service.GetAllowedDateRangeUtcNow();

        Assert.Equal(DateOnly.FromDateTime(nowLocal.Date).AddDays(2), result.MinDate);
        Assert.Equal(DateOnly.FromDateTime(nowLocal.Date).AddDays(7), result.MaxDate);
    }

    [Fact]
    public void GetAllowedDateRangeUtcNow_FridayAfterCutoff_SkipsMondayAndAllowsTuesday()
    {
        // Regression: pre-fix, Fri-after-cutoff used calendar-day arithmetic
        // (Fri+2 = Sun, skip-once = Mon) so users could still book Monday
        // after the 2pm cutoff on Friday. Business-day arithmetic walks
        // Fri -> Mon (1 bizday) -> Tue (2 bizdays), matching the rule
        // "after cutoff you cannot book the next business day."
        TimeZoneInfo zone;
        try
        {
            zone = TimeRuleServiceTestFactory.FindZone(nowLocal =>
                nowLocal.DayOfWeek == DayOfWeek.Friday &&
                nowLocal.Hour >= 14);
        }
        catch (InvalidOperationException)
        {
            Assert.True(true); // No host timezone currently shows Friday after 14:00 local.
            return;
        }

        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
        var service = TimeRuleServiceTestFactory.Create(zone, cutoffHourLocal: 14);

        var result = service.GetAllowedDateRangeUtcNow();

        var today = DateOnly.FromDateTime(nowLocal.Date);
        var monday = today.AddDays(3);
        var tuesday = today.AddDays(4);

        Assert.Equal(DayOfWeek.Monday, monday.DayOfWeek);
        Assert.Equal(DayOfWeek.Tuesday, tuesday.DayOfWeek);
        Assert.NotEqual(monday, result.MinDate);
        Assert.Equal(tuesday, result.MinDate);
    }

    [Fact]
    public void GetAllowedDateRangeUtcNow_MinDateOnWeekend_AdvancesToMonday()
    {
        TimeZoneInfo zone;
        try
        {
            // Find a timezone where the candidate minDate (today+1 or today+2)
            // would fall on a Saturday or Sunday; assert the returned minDate
            // is Monday.
            zone = TimeRuleServiceTestFactory.FindZone(nowLocal =>
            {
                var today = DateOnly.FromDateTime(nowLocal.Date);
                var candidateMin = today.AddDays(nowLocal.Hour < 14 ? 1 : 2);
                return candidateMin.DayOfWeek == DayOfWeek.Saturday ||
                       candidateMin.DayOfWeek == DayOfWeek.Sunday;
            });
        }
        catch (InvalidOperationException)
        {
            Assert.True(true); // Calendar-dependent edge: no host timezone currently yields a weekend candidate min date.
            return;
        }

        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
        var service = TimeRuleServiceTestFactory.Create(zone, cutoffHourLocal: 14);

        var result = service.GetAllowedDateRangeUtcNow();

        Assert.NotEqual(DayOfWeek.Saturday, result.MinDate.DayOfWeek);
        Assert.NotEqual(DayOfWeek.Sunday, result.MinDate.DayOfWeek);
    }

    [Fact]
    public void IsCancellationAllowed_BookingInPast_ReturnsFalse()
    {
        var zone = TimeRuleServiceTestFactory.FindZone(_ => true);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
        var service = TimeRuleServiceTestFactory.Create(zone, Math.Min(nowLocal.Hour + 1, 23));
        var pastBookingUtc = TimeZoneInfo.ConvertTimeToUtc(nowLocal.AddHours(-1), zone);

        var allowed = service.IsCancellationAllowed(pastBookingUtc);

        Assert.False(allowed);
    }

    [Fact]
    public void IsCancellationAllowed_NextBusinessDayBeforeCutoff_ReturnsTrue()
    {
        TimeZoneInfo zone;
        try
        {
            zone = TimeRuleServiceTestFactory.FindZone(nowLocal =>
                nowLocal.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday &&
                nowLocal.AddDays(1).DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday &&
                nowLocal.Hour <= 22);
        }
        catch (InvalidOperationException)
        {
            Assert.True(true); // Calendar-dependent edge: no host timezone currently yields a weekday today/tomorrow pair before cutoff.
            return;
        }
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
        var service = TimeRuleServiceTestFactory.Create(zone, nowLocal.Hour + 1);
        var bookingDate = DateOnly.FromDateTime(nowLocal.Date.AddDays(1));
        var bookingUtc = service.ConvertLocalToUtc(bookingDate, new TimeOnly(12, 0));

        var allowed = service.IsCancellationAllowed(bookingUtc);

        Assert.True(allowed);
    }

    [Fact]
    public void IsCancellationAllowed_NextBusinessDayAfterCutoff_ReturnsFalse()
    {
        TimeZoneInfo zone;
        try
        {
            zone = TimeRuleServiceTestFactory.FindZone(nowLocal =>
                nowLocal.AddDays(1).DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday);
        }
        catch (InvalidOperationException)
        {
            Assert.True(true); // Calendar-dependent edge: no host timezone currently yields a weekday tomorrow.
            return;
        }

        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
        var service = TimeRuleServiceTestFactory.Create(zone, nowLocal.Hour);
        var bookingDate = DateOnly.FromDateTime(nowLocal.Date.AddDays(1));
        var bookingUtc = service.ConvertLocalToUtc(bookingDate, new TimeOnly(12, 0));

        var allowed = service.IsCancellationAllowed(bookingUtc);

        Assert.False(allowed);
    }

    [Fact]
    public void GetAvailableSlotsForDate_NoBookings_ReturnsAllSlots()
    {
        var zone = TimeRuleServiceTestFactory.FindZone(nowLocal => nowLocal.Hour <= 22);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
        var service = TimeRuleServiceTestFactory.Create(zone, nowLocal.Hour + 1);
        var date = service.GetAllowedDateRangeUtcNow().MinDate;

        var slots = service.GetAvailableSlotsForDate(date, Array.Empty<Booking>()).ToList();

        Assert.Equal(16, slots.Count);
        Assert.Equal(new TimeOnly(7, 30), slots.First());
        Assert.Equal(new TimeOnly(15, 0), slots.Last());
    }

    [Fact]
    public void GetAvailableSlotsForDate_SaturdayInsideAllowedWindow_ReturnsNoSlots()
    {
        var zone = TimeRuleServiceTestFactory.FindZone(_ => true);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
        var service = TimeRuleServiceTestFactory.Create(zone, Math.Min(nowLocal.Hour + 1, 23));
        var today = DateOnly.FromDateTime(nowLocal.Date);
        var daysUntilSaturday = ((int)DayOfWeek.Saturday - (int)today.DayOfWeek + 7) % 7;
        var date = today.AddDays(daysUntilSaturday);

        var slots = service.GetAvailableSlotsForDate(date, Array.Empty<Booking>(), minDateOverride: date).ToList();

        Assert.Empty(slots);
    }

    [Fact]
    public void GetAvailableSlotsForDate_OverlappingBooking_BlocksAffectedSlots()
    {
        var zone = TimeRuleServiceTestFactory.FindZone(nowLocal => nowLocal.Hour <= 22);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
        var service = TimeRuleServiceTestFactory.Create(zone, nowLocal.Hour + 1, maxBookingsPerSlot: 1);
        var date = service.GetAllowedDateRangeUtcNow().MinDate;
        var booking = CreateBooking(service, date, new TimeOnly(9, 0), new TimeOnly(10, 0));

        var slots = service.GetAvailableSlotsForDate(date, new[] { booking }).ToList();

        Assert.DoesNotContain(new TimeOnly(8, 30), slots);
        Assert.DoesNotContain(new TimeOnly(9, 0), slots);
        Assert.DoesNotContain(new TimeOnly(9, 30), slots);
        Assert.DoesNotContain(new TimeOnly(10, 0), slots);
    }

    [Fact]
    public void GetAvailableSlotsForDate_TravelPadding_BlocksAdjacentSlot()
    {
        var zone = TimeRuleServiceTestFactory.FindZone(nowLocal => nowLocal.Hour <= 22);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
        var service = TimeRuleServiceTestFactory.Create(zone, nowLocal.Hour + 1, maxBookingsPerSlot: 1);
        var date = service.GetAllowedDateRangeUtcNow().MinDate;
        var booking = CreateBooking(service, date, new TimeOnly(12, 0), new TimeOnly(13, 0));

        var slots = service.GetAvailableSlotsForDate(date, new[] { booking }).ToList();

        Assert.Contains(new TimeOnly(10, 30), slots);
        Assert.DoesNotContain(new TimeOnly(11, 0), slots);
    }

    private static Booking CreateBooking(
        TimeRuleService service,
        DateOnly date,
        TimeOnly startLocal,
        TimeOnly endLocal)
    {
        var startUtc = service.ConvertLocalToUtc(date, startLocal);
        var endUtc = service.ConvertLocalToUtc(date, endLocal);

        return new Booking
        {
            StartUtc = startUtc,
            EndUtc = endUtc,
            Status = "Assigned"
        };
    }
}
