using System;

namespace Kor.Inspections.App.Data.Models
{
    public class Booking
    {
        public Guid BookingId { get; set; }

        // Project / job details
        public string ProjectNumber { get; set; } = string.Empty;
        public string? ProjectAddress { get; set; }

        // Work details
        public string? ElementsJson { get; set; }

        // Site contact
        public string ContactName { get; set; } = string.Empty;
        public string ContactPhone { get; set; } = string.Empty;

        // NEW: submitter contact email
        public string ContactEmail { get; set; } = string.Empty;

        // Optional notes
        public string? Comments { get; set; }

        // Scheduling
        public DateTime StartUtc { get; set; }
        public DateTime EndUtc { get; set; }
        public string? TimePreference { get; set; }




        // Lifecycle / workflow
        public string Status { get; set; } = "Unassigned";
        public string? AssignedTo { get; set; }

        // Audit
        public DateTime CreatedUtc { get; set; }

        // Cancellation
        public Guid CancelToken { get; set; }
    }
}
