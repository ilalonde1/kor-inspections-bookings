using System;

namespace Kor.Inspections.App.Data.Models
{
    /// <summary>
    /// Persistent verification state for the email-OTP flow in
    /// ProjectBootstrapVerificationService. Previously this lived in
    /// IMemoryCache which was wiped on app-pool recycle (default IIS
    /// 29-hour recycle). Moving it to a DB-backed entity keeps users
    /// verified across recycles and enables multi-instance deployments.
    /// </summary>
    public class ProjectVerification
    {
        public int Id { get; set; }

        // Scope — normalized: ProjectNumber is Base5, Email is lowercased.
        public string ProjectNumber { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        // OTP + lifecycle.
        public string Code { get; set; } = string.Empty;
        public bool Verified { get; set; }
        public int FailedAttempts { get; set; }

        // ExpiresUtc is the single source of truth for whether the row
        // is still valid. It carries the pending TTL while Verified=false,
        // and is refreshed to the verified TTL when the code is accepted.
        public DateTime ExpiresUtc { get; set; }

        // Audit.
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }
}
