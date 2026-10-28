using Kor.Inspections.App.Services;
using Kor.Inspections.Tests.Helpers;

namespace Kor.Inspections.Tests.Services;

/// <summary>
/// Booking-notice rules with BC statutory holidays (Griffin, Oct 2026). All times
/// are Pacific local with a pinned clock and the 2 p.m. cutoff; Boxing Day is a
/// configured closure as in appsettings.json.
/// </summary>
public class BusinessDayRuleTests
{
    private static readonly TimeZoneInfo Pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
    private static readonly List<string> Closures = ["12-26"];

    private static TimeRuleService At(string localNow) =>
        TimeRuleServiceTestFactory.CreateAt(Pacific, DateTime.Parse(localNow), closedDates: Closures);

    // Griffin's table of expected behaviour, on real 2026 dates.
    [Theory]
    [InlineData("2026-11-02 10:00", "2026-11-03")] // Monday 10am -> Tuesday
    [InlineData("2026-11-02 14:00", "2026-11-04")] // Monday 2pm -> Wednesday
    [InlineData("2026-11-06 10:00", "2026-11-09")] // Friday 10am -> Monday
    [InlineData("2026-11-06 14:30", "2026-11-10")] // Friday 2:30pm -> Tuesday
    [InlineData("2026-11-07 10:00", "2026-11-10")] // Saturday, normal week -> Tuesday
    [InlineData("2026-11-08 20:00", "2026-11-10")] // Sunday, normal week -> Tuesday
    [InlineData("2026-10-10 09:17", "2026-10-14")] // Saturday before Thanksgiving -> Wednesday (the Oct 10 incident)
    [InlineData("2026-10-11 12:00", "2026-10-14")] // Sunday before Thanksgiving -> Wednesday
    [InlineData("2026-10-09 10:00", "2026-10-13")] // Friday before 2pm, Monday holiday -> Tuesday
    [InlineData("2026-10-09 14:01", "2026-10-14")] // Friday after 2pm, Monday holiday -> Wednesday
    [InlineData("2026-10-12 08:00", "2026-10-14")] // Monday holiday, morning -> Wednesday
    [InlineData("2026-10-12 16:00", "2026-10-14")] // Monday holiday, afternoon -> Wednesday
    public void MinDate_MatchesGriffinsTable(string localNow, string expectedMin)
    {
        Assert.Equal(DateOnly.Parse(expectedMin), At(localNow).GetAllowedDateRangeUtcNow().MinDate);
    }

    [Theory]
    [InlineData("2026-11-10 10:00", "2026-11-12")] // Tue before cutoff; Wed Nov 11 Remembrance Day -> Thu
    [InlineData("2026-11-10 15:00", "2026-11-13")] // Tue after cutoff -> Fri
    [InlineData("2026-11-11 10:00", "2026-11-13")] // on the midweek holiday itself -> Fri
    [InlineData("2026-09-29 10:00", "2026-10-01")] // Tue before Sep 30 (Truth and Reconciliation) -> Thu
    [InlineData("2026-04-02 10:00", "2026-04-06")] // Thu before Good Friday -> Easter Monday (not a BC stat)
    [InlineData("2026-04-02 15:00", "2026-04-07")] // Thu after cutoff before Good Friday -> Tue
    public void MinDate_MidweekAndEasterHolidays(string localNow, string expectedMin)
    {
        Assert.Equal(DateOnly.Parse(expectedMin), At(localNow).GetAllowedDateRangeUtcNow().MinDate);
    }

    [Theory]
    // 2026: Christmas Fri, Boxing Day Sat (observed Mon Dec 28), New Year's Fri.
    [InlineData("2026-12-23 10:00", "2026-12-24")]
    [InlineData("2026-12-23 15:00", "2026-12-29")] // Thu 24 next; cutoff blocks it; 25/26/27/28 closed -> Tue 29
    [InlineData("2026-12-24 10:00", "2026-12-29")]
    [InlineData("2026-12-24 15:00", "2026-12-30")]
    [InlineData("2026-12-26 10:00", "2026-12-30")] // Boxing Day itself (Sat)
    [InlineData("2026-12-31 10:00", "2027-01-04")] // year-end: Jan 1 closed -> Mon Jan 4
    [InlineData("2026-12-31 15:00", "2027-01-05")]
    // 2027: Christmas Sat (observed Mon 27), Boxing Day Sun (observed Tue 28).
    [InlineData("2027-12-24 10:00", "2027-12-29")]
    [InlineData("2027-12-24 15:00", "2027-12-30")]
    public void MinDate_ChristmasBoxingDayNewYear(string localNow, string expectedMin)
    {
        Assert.Equal(DateOnly.Parse(expectedMin), At(localNow).GetAllowedDateRangeUtcNow().MinDate);
    }

    [Theory]
    // DST ends Sun 2026-11-01 (UTC-7 -> UTC-8). 21:59Z on Mon Nov 2 is 1:59pm PST
    // (before cutoff); if the old PDT offset were wrongly used it would be 2:59pm.
    [InlineData("2026-11-02T21:59:00Z", "2026-11-03")]
    [InlineData("2026-11-02T22:00:00Z", "2026-11-04")]
    // Friday before the change, still PDT: 20:59Z = 1:59pm.
    [InlineData("2026-10-30T20:59:00Z", "2026-11-02")]
    [InlineData("2026-10-30T21:00:00Z", "2026-11-03")]
    // DST starts Sun 2026-03-08 (UTC-8 -> UTC-7). 20:59Z on Mon Mar 9 is 1:59pm PDT.
    [InlineData("2026-03-09T20:59:00Z", "2026-03-10")]
    [InlineData("2026-03-09T21:00:00Z", "2026-03-11")]
    public void MinDate_UsesPacificOffsetAcrossDaylightSavingChanges(string utcNow, string expectedMin)
    {
        var service = TimeRuleServiceTestFactory.CreateAtUtc(
            Pacific, DateTime.Parse(utcNow, null, System.Globalization.DateTimeStyles.AdjustToUniversal));

        Assert.Equal(DateOnly.Parse(expectedMin), service.GetAllowedDateRangeUtcNow().MinDate);
    }

    [Fact]
    public void GetAvailableSlotsForDate_Holiday_PublicGetsNoSlotsAdminDoes()
    {
        var service = At("2026-11-09 09:00"); // Mon; Wed Nov 11 is Remembrance Day
        var holiday = new DateOnly(2026, 11, 11);
        var adminMin = service.GetNonBusinessDayMinDateUtcNow();

        Assert.Empty(service.GetAvailableSlotsForDate(holiday, []));
        Assert.NotEmpty(service.GetAvailableSlotsForDate(holiday, [], minDateOverride: adminMin, allowNonBusinessDays: true));
    }

    [Theory]
    // Tuesday-after-Thanksgiving booking: notice is due Fri Oct 9 at 2pm (Mon is a holiday).
    [InlineData("2026-10-09 13:59", true)]
    [InlineData("2026-10-09 14:01", false)]
    [InlineData("2026-10-10 10:00", false)] // weekend
    [InlineData("2026-10-12 09:00", false)] // on the holiday Monday
    public void IsCancellationAllowed_PreviousBusinessDaySkipsHolidays(string localNow, bool expected)
    {
        var service = At(localNow);
        var bookingStartUtc = service.ConvertLocalToUtc(new DateOnly(2026, 10, 13), new TimeOnly(11, 0));

        Assert.Equal(expected, service.IsCancellationAllowed(bookingStartUtc));
    }

    [Fact]
    public void IsBusinessDay_LateDecemberClosureObservedInJanuary()
    {
        // A configured 12-31 closure in 2028 falls on Sunday; Mon Jan 1 2029 is
        // New Year's, so the closure is observed on Tue Jan 2 2029.
        var service = TimeRuleServiceTestFactory.CreateAt(
            Pacific, new DateTime(2028, 12, 20, 10, 0, 0), closedDates: ["12-31"]);

        Assert.False(service.IsBusinessDay(new DateOnly(2029, 1, 1)));  // New Year's (Mon)
        Assert.False(service.IsBusinessDay(new DateOnly(2029, 1, 2)));  // observed 12-31 closure
        Assert.True(service.IsBusinessDay(new DateOnly(2029, 1, 3)));
    }

    [Fact]
    public void GetNonBusinessDays_ListsWeekendsAndHolidaysInWindow()
    {
        var days = At("2026-10-07 10:00").GetNonBusinessDays(new DateOnly(2026, 10, 8), new DateOnly(2026, 10, 14));

        Assert.Equal(
            new[] { new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 11), new DateOnly(2026, 10, 12) },
            days);
    }
}
