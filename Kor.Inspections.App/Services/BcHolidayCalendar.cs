using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Kor.Inspections.App.Services
{
    /// <summary>
    /// Days the office is closed for field reviews: British Columbia statutory
    /// holidays (Employment Standards Act) plus configured company closures.
    /// Computed per year from fixed rules, so it needs no yearly maintenance.
    /// </summary>
    /// <remarks>
    /// Deliberately not a generic holiday library: BC does NOT observe Boxing Day
    /// or Easter Monday as statutory holidays, and added the National Day for
    /// Truth and Reconciliation (Sep 30) in 2023. Boxing Day is a company closure
    /// configured via InspectionRules:ClosedDates.
    /// </remarks>
    public static class BcHolidayCalendar
    {
        public static IReadOnlyList<(DateOnly Date, string Name)> GetStatutoryHolidays(int year)
        {
            var holidays = new List<(DateOnly, string)>
            {
                (new DateOnly(year, 1, 1), "New Year's Day"),
                (NthWeekday(year, 2, DayOfWeek.Monday, 3), "Family Day"),
                (EasterSunday(year).AddDays(-2), "Good Friday"),
                (MondayBefore(new DateOnly(year, 5, 25)), "Victoria Day"),
                (new DateOnly(year, 7, 1), "Canada Day"),
                (NthWeekday(year, 8, DayOfWeek.Monday, 1), "B.C. Day"),
                (NthWeekday(year, 9, DayOfWeek.Monday, 1), "Labour Day"),
                (NthWeekday(year, 10, DayOfWeek.Monday, 2), "Thanksgiving Day"),
                (new DateOnly(year, 11, 11), "Remembrance Day"),
                (new DateOnly(year, 12, 25), "Christmas Day"),
            };

            if (year >= 2023)
                holidays.Add((new DateOnly(year, 9, 30), "National Day for Truth and Reconciliation"));

            return holidays.OrderBy(h => h.Item1).ToList();
        }

        /// <summary>
        /// Weekdays in <paramref name="year"/> with no field reviews. A holiday or
        /// annual closure that falls on a weekend is observed on the next weekday
        /// that isn't already closed (e.g. Christmas on Saturday -> Monday, Boxing
        /// Day on Sunday -> Tuesday).
        /// </summary>
        /// <param name="closedDates">
        /// Extra closures: "MM-dd" repeats every year (observed rule applies);
        /// "yyyy-MM-dd" is a one-off date.
        /// </param>
        public static HashSet<DateOnly> GetClosedWeekdays(int year, IEnumerable<string>? closedDates)
        {
            var annual = GetStatutoryHolidays(year).Select(h => h.Date).ToList();
            var oneOff = new List<DateOnly>();

            foreach (var entry in closedDates ?? Enumerable.Empty<string>())
            {
                if (!TryParseClosedDate(entry, out var month, out var day, out var specificDate))
                    throw new FormatException($"Invalid InspectionRules:ClosedDates entry '{entry}'. Use MM-dd or yyyy-MM-dd.");

                if (specificDate is { } d)
                {
                    if (d.Year == year)
                        oneOff.Add(d);
                }
                else if (DateTime.DaysInMonth(year, month) >= day)
                {
                    annual.Add(new DateOnly(year, month, day));
                }
            }

            var closed = new HashSet<DateOnly>();
            foreach (var date in annual.Distinct().OrderBy(d => d))
            {
                var observed = date;
                while (IsWeekend(observed) || closed.Contains(observed))
                    observed = observed.AddDays(1);
                closed.Add(observed);
            }

            foreach (var date in oneOff)
            {
                if (!IsWeekend(date))
                    closed.Add(date);
            }

            return closed;
        }

        public static bool TryParseClosedDate(string? entry, out int month, out int day, out DateOnly? specificDate)
        {
            month = day = 0;
            specificDate = null;
            var value = (entry ?? string.Empty).Trim();

            if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                specificDate = date;
                return true;
            }

            // Leap year so "02-29" parses; it is skipped in non-leap years.
            if (DateOnly.TryParseExact("2024-" + value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var annual))
            {
                month = annual.Month;
                day = annual.Day;
                return true;
            }

            return false;
        }

        /// <summary>Gregorian Easter Sunday (anonymous Gregorian algorithm).</summary>
        public static DateOnly EasterSunday(int year)
        {
            int a = year % 19;
            int b = year / 100;
            int c = year % 100;
            int d = b / 4;
            int e = b % 4;
            int f = (b + 8) / 25;
            int g = (b - f + 1) / 3;
            int h = (19 * a + b - d - g + 15) % 30;
            int i = c / 4;
            int k = c % 4;
            int l = (32 + 2 * e + 2 * i - h - k) % 7;
            int m = (a + 11 * h + 22 * l) / 451;
            int month = (h + l - 7 * m + 114) / 31;
            int day = ((h + l - 7 * m + 114) % 31) + 1;
            return new DateOnly(year, month, day);
        }

        private static bool IsWeekend(DateOnly date) =>
            date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

        private static DateOnly NthWeekday(int year, int month, DayOfWeek dayOfWeek, int n)
        {
            var first = new DateOnly(year, month, 1);
            var offset = ((int)dayOfWeek - (int)first.DayOfWeek + 7) % 7;
            return first.AddDays(offset + 7 * (n - 1));
        }

        // Victoria Day: the last Monday strictly before May 25.
        private static DateOnly MondayBefore(DateOnly date)
        {
            var d = date.AddDays(-1);
            while (d.DayOfWeek != DayOfWeek.Monday)
                d = d.AddDays(-1);
            return d;
        }
    }
}
