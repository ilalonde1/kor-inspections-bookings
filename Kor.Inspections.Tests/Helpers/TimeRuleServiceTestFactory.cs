using Kor.Inspections.App.Options;
using Kor.Inspections.App.Services;
using Microsoft.Extensions.Options;

namespace Kor.Inspections.Tests.Helpers;

internal static class TimeRuleServiceTestFactory
{
    public static TimeRuleService Create(
        TimeZoneInfo timeZone,
        int cutoffHourLocal,
        int maxBookingsPerSlot = 3,
        int defaultDurationMinutes = 60)
    {
        var options = Options.Create(new InspectionRulesOptions
        {
            CutoffHourLocal = cutoffHourLocal,
            BookingWindowDays = 7,
            SlotMinutes = 30,
            DefaultDurationMinutes = defaultDurationMinutes,
            TravelPaddingMinutes = 15,
            MaxBookingsPerSlot = maxBookingsPerSlot,
            WorkStart = "07:30",
            WorkEnd = "16:00",
            TimeZoneId = timeZone.Id
        });

        return new TimeRuleService(options);
    }

    /// <summary>
    /// Finds a system timezone where the supplied predicate evaluates true
    /// for the current UTC instant.
    /// </summary>
    /// <remarks>
    /// Beware: predicates that constrain <c>nowLocal.DayOfWeek</c> directly are
    /// unsatisfiable on weekend afternoons in UTC - no timezone can roll
    /// the clock back to a weekday. If the test is really asking "tomorrow
    /// is a business day", check <c>nowLocal.AddDays(1).DayOfWeek</c> instead.
    /// </remarks>
    public static TimeZoneInfo FindZone(Func<DateTime, bool> predicate)
    {
        foreach (var zone in TimeZoneInfo.GetSystemTimeZones())
        {
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
            if (predicate(nowLocal))
                return zone;
        }

        throw new InvalidOperationException("Could not find a timezone that satisfies the test precondition.");
    }

    /// <summary>
    /// Non-throwing variant: returns true if a zone satisfying <paramref name="predicate"/>
    /// exists at the current UTC instant, false otherwise. Use to drive the skip
    /// pattern in tests whose preconditions are calendar-dependent (e.g. "tomorrow
    /// is a weekday" is unsatisfiable late on Friday UTC).
    /// </summary>
    public static bool TryFindZone(Func<DateTime, bool> predicate, out TimeZoneInfo zone)
    {
        foreach (var z in TimeZoneInfo.GetSystemTimeZones())
        {
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, z);
            if (predicate(nowLocal))
            {
                zone = z;
                return true;
            }
        }

        zone = TimeZoneInfo.Utc;
        return false;
    }
}
