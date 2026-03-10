using System;

namespace Kor.Inspections.App.Data.Models
{
    public class ProjectAccess
    {
        public Guid Id { get; set; }

        public string ProjectNumber { get; set; } = string.Empty;

        // SHA-256 hash of the PIN as bytes
        public byte[] PinHash { get; set; } = Array.Empty<byte>();

        public bool IsEnabled { get; set; } = true;

        public DateTime CreatedUtc { get; set; }

        public DateTime? LastUsedUtc { get; set; }
    }
}
