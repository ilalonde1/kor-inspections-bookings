using System.ComponentModel.DataAnnotations;

namespace Kor.Inspections.App.Options
{
    public class InspectionRulesOptions
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
    }
}
