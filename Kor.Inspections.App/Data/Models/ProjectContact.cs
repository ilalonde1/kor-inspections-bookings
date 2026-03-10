using System;

namespace Kor.Inspections.App.Data.Models
{
    public class ProjectContact
    {
        public int ContactId { get; set; }

        // Scope
        public string ProjectNumber { get; set; } = string.Empty;
        public string EmailDomain { get; set; } = string.Empty;

        // Contact details
        public string ContactName { get; set; } = string.Empty;
        public string ContactPhone { get; set; } = string.Empty;
        public string ContactEmail { get; set; } = string.Empty;
        public string? ContactAddress { get; set; }

        // Soft delete (never hard delete to avoid history issues)
        public bool IsDeleted { get; set; }

        // Audit
        public DateTime UpdatedUtc { get; set; }
    }
}
