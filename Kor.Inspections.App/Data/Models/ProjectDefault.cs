using System;

namespace Kor.Inspections.App.Data.Models
{
    public class ProjectDefault
    {
        public int Id { get; set; }
        // Scope
        public string ProjectNumber { get; set; } = string.Empty;
        public string EmailDomain { get; set; } = string.Empty;

        // Default values for this project + domain
        public string? DefaultAddress { get; set; }

        // Audit
        public DateTime UpdatedUtc { get; set; }
    }
}
