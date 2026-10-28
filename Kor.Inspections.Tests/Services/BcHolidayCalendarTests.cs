using Kor.Inspections.App.Options;
using Kor.Inspections.App.Services;
using System.ComponentModel.DataAnnotations;

namespace Kor.Inspections.Tests.Services;

public class BcHolidayCalendarTests
{
    [Fact]
    public void GetStatutoryHolidays_2026_MatchesBcEmploymentStandardsList()
    {
        var expected = new[]
        {
            "2026-01-01", // New Year's Day
            "2026-02-16", // Family Day (3rd Mon Feb)
            "2026-04-03", // Good Friday
            "2026-05-18", // Victoria Day (Mon before May 25)
            "2026-07-01", // Canada Day
            "2026-08-03", // B.C. Day (1st Mon Aug)
            "2026-09-07", // Labour Day
            "2026-09-30", // National Day for Truth and Reconciliation
            "2026-10-12", // Thanksgiving (2nd Mon Oct)
            "2026-11-11", // Remembrance Day
            "2026-12-25", // Christmas Day
        }.Select(DateOnly.Parse);

        Assert.Equal(expected, BcHolidayCalendar.GetStatutoryHolidays(2026).Select(h => h.Date));
    }

    [Fact]
    public void GetStatutoryHolidays_ExcludesNonBcHolidays()
    {
        var dates = BcHolidayCalendar.GetStatutoryHolidays(2026).Select(h => h.Date).ToHashSet();

        Assert.DoesNotContain(new DateOnly(2026, 12, 26), dates); // Boxing Day: federal, not BC stat
        Assert.DoesNotContain(new DateOnly(2026, 4, 6), dates);   // Easter Monday
    }

    [Fact]
    public void GetStatutoryHolidays_TruthAndReconciliationOnlyFrom2023()
    {
        Assert.DoesNotContain(BcHolidayCalendar.GetStatutoryHolidays(2022), h => h.Date == new DateOnly(2022, 9, 30));
        Assert.Contains(BcHolidayCalendar.GetStatutoryHolidays(2023), h => h.Date == new DateOnly(2023, 9, 30));
    }

    [Theory]
    [InlineData(2024, "2024-03-31")]
    [InlineData(2025, "2025-04-20")]
    [InlineData(2026, "2026-04-05")]
    [InlineData(2027, "2027-03-28")]
    [InlineData(2028, "2028-04-16")]
    [InlineData(2029, "2029-04-01")]
    [InlineData(2030, "2030-04-21")]
    public void EasterSunday_KnownYears(int year, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected), BcHolidayCalendar.EasterSunday(year));
    }

    [Theory]
    [InlineData(2027, "2027-05-24")] // May 25 is a Tuesday
    [InlineData(2026, "2026-05-18")] // May 25 is itself a Monday -> previous Monday
    public void VictoriaDay_IsMondayStrictlyBeforeMay25(int year, string expected)
    {
        Assert.Contains(BcHolidayCalendar.GetStatutoryHolidays(year), h => h.Name == "Victoria Day" && h.Date == DateOnly.Parse(expected));
    }

    [Fact]
    public void GetClosedWeekdays_2027_ChristmasAndBoxingOnWeekend_ObservedMondayAndTuesday()
    {
        // Christmas 2027 is a Saturday, Boxing Day a Sunday.
        var closed = BcHolidayCalendar.GetClosedWeekdays(2027, ["12-26"]);

        Assert.Contains(new DateOnly(2027, 12, 27), closed);
        Assert.Contains(new DateOnly(2027, 12, 28), closed);
        Assert.DoesNotContain(new DateOnly(2027, 12, 29), closed);
    }

    [Fact]
    public void GetClosedWeekdays_2028_SaturdayHolidaysObservedOnMonday()
    {
        var closed = BcHolidayCalendar.GetClosedWeekdays(2028, null);

        Assert.Contains(new DateOnly(2028, 1, 3), closed);   // New Year's (Sat)
        Assert.Contains(new DateOnly(2028, 7, 3), closed);   // Canada Day (Sat)
        Assert.Contains(new DateOnly(2028, 11, 13), closed); // Remembrance Day (Sat)
    }

    [Fact]
    public void GetClosedWeekdays_CanadaDayOnSunday_ObservedMonday()
    {
        Assert.Contains(new DateOnly(2029, 7, 2), BcHolidayCalendar.GetClosedWeekdays(2029, null));
    }

    [Fact]
    public void GetClosedWeekdays_2026_BoxingDayOnSaturday_ObservedMonday()
    {
        var closed = BcHolidayCalendar.GetClosedWeekdays(2026, ["12-26"]);

        Assert.Contains(new DateOnly(2026, 12, 25), closed); // Christmas (Fri)
        Assert.Contains(new DateOnly(2026, 12, 28), closed); // Boxing Day (Sat) observed
    }

    [Fact]
    public void GetClosedWeekdays_OneOffDate_OnlyAppliesToThatYear()
    {
        Assert.Contains(new DateOnly(2026, 12, 24), BcHolidayCalendar.GetClosedWeekdays(2026, ["2026-12-24"]));
        Assert.DoesNotContain(new DateOnly(2027, 12, 24), BcHolidayCalendar.GetClosedWeekdays(2027, ["2026-12-24"]));
    }

    [Theory]
    [InlineData("12-26", true)]
    [InlineData("2026-12-24", true)]
    [InlineData("02-29", true)]
    [InlineData("13-01", false)]
    [InlineData("Dec 26", false)]
    [InlineData("", false)]
    public void InspectionRulesOptions_ValidatesClosedDates(string entry, bool valid)
    {
        var options = new InspectionRulesOptions { ClosedDates = [entry] };
        var results = new List<ValidationResult>();

        var ok = Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        Assert.Equal(valid, ok);
    }
}
