using Kor.Inspections.App.Data.Models;
using Kor.Inspections.App.Services;
using Kor.Inspections.Tests.Helpers;

namespace Kor.Inspections.Tests.Services;

public class TimeRuleServiceTests
{
    [Fact]
    public void GetAllowedDateRangeUtcNow_BeforeCutoffHour_UsesTomorrowAsMinDate()
    {
        var zone = TimeRuleServiceTestFactory.FindZone(nowLocal => nowLocal.Hour <= 22);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
        var service = TimeRuleServiceTestFactory.Create(zone, nowLocal.Hour + 1);

        var result = service.GetAllowedDateRangeUtcNow();

        Assert.Equal(DateOnly.FromDateTime(nowLocal.Date).AddDays(1), result.MinDate);
        Assert.Equal(DateOnly.FromDateTime(nowLocal.Date).AddDays(7), result.MaxDate);
    }

    [Fact]
    public void GetAllowedDateRangeUtcNow_AfterCutoffHour_UsesDayAfterTomorrowAsMinDate()
    {
        var zone = TimeRuleServiceTestFactory.FindZone(_ => true);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
        var service = TimeRuleServiceTestFactory.Create(zone, nowLocal.Hour);

        var result = service.GetAllowedDateRangeUtcNow();

        Assert.Equal(DateOnly.FromDateTime(nowLocal.Date).AddDays(2), result.MinDate);
        Assert.Equal(DateOnly.FromDateTime(nowLocal.Date).AddDays(7), result.MaxDate);
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
        var zone = TimeRuleServiceTestFactory.FindZone(nowLocal =>
            nowLocal.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday &&
            nowLocal.Hour <= 22);
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
        var zone = TimeRuleServiceTestFactory.FindZone(nowLocal =>
            nowLocal.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday);
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
