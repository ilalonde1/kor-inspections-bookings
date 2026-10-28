using System.ComponentModel.DataAnnotations;
using Kor.Inspections.App.Services;

namespace Kor.Inspections.App.Options
{
    public class InspectionRulesOptions : IValidatableObject
    {
        public int CutoffHourLocal { get; set; } = 14;
        public int BookingWindowDays { get; set; } = 7;
        public int SlotMinutes { get; set; } = 30;
        public int DefaultDurationMinutes { get; set; } = 60;
        public int TravelPaddingMinutes { get; set; } = 15;
        public int MaxBookingsPerSlot { get; set; } = 3;

        [Required]
        public string WorkStart { get; set; } = "07:30";

        [Required]
        public string WorkEnd { get; set; } = "16:00";

        [Required]
        public string TimeZoneId { get; set; } = "Pacific Standard Time";

        /// <summary>
        /// Company closures on top of BC statutory holidays (which are built in).
        /// "MM-dd" repeats yearly and moves to the next weekday if it falls on a
        /// weekend; "yyyy-MM-dd" is a one-off date.
        /// </summary>
        public List<string> ClosedDates { get; set; } = new();

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            foreach (var entry in ClosedDates)
            {
                if (!BcHolidayCalendar.TryParseClosedDate(entry, out _, out _, out _))
                {
                    yield return new ValidationResult(
                        $"InspectionRules:ClosedDates entry '{entry}' is invalid. Use MM-dd or yyyy-MM-dd.",
                        new[] { nameof(ClosedDates) });
                }
            }
        }
    }
}
