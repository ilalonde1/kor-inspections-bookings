using Kor.Inspections.App.Options;
using Kor.Inspections.App.Services;
using Microsoft.Extensions.Options;

namespace Kor.Inspections.Tests.Helpers;

internal static class TimeRuleServiceTestFactory
{
    public static TimeRuleService Create(
        TimeZoneInfo timeZone,
        int cutoffHourLocal,
        int maxBookingsPerSlot = 3)
    {
        var options = Options.Create(new InspectionRulesOptions
        {
            CutoffHourLocal = cutoffHourLocal,
            BookingWindowDays = 7,
            SlotMinutes = 30,
            DefaultDurationMinutes = 60,
            TravelPaddingMinutes = 15,
            MaxBookingsPerSlot = maxBookingsPerSlot,
            WorkStart = "07:30",
            WorkEnd = "16:00",
            TimeZoneId = timeZone.Id
        });

        return new TimeRuleService(options);
    }

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
}
