using System;

namespace Kor.Inspections.App.Data
{
    public class BookingAction
    {
        public int ActionId { get; set; }
        public Guid BookingId { get; set; }

        public string ActionType { get; set; } = string.Empty;
        public string? PerformedBy { get; set; }
        public string? Notes { get; set; }

        public DateTime ActionUtc { get; set; }
    }
}
