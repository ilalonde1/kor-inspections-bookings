using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Kor.Inspections.App.Data;
using Kor.Inspections.App.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kor.Inspections.App.Services
{
    public class ProjectAccessService
    {
        private readonly InspectionsContext _db;
        private readonly ILogger<ProjectAccessService> _logger;

        public ProjectAccessService(InspectionsContext db, ILogger<ProjectAccessService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public Task<ProjectAccess?> GetByProjectNumberAsync(string projectNumber)
        {
            if (string.IsNullOrWhiteSpace(projectNumber))
                throw new ArgumentException("Project number is required.", nameof(projectNumber));

            var normalized = NormalizeProjectNumber(projectNumber);

            return _db.ProjectAccessEntries
                .FirstOrDefaultAsync(pa => pa.ProjectNumber == normalized);
        }

        /// <summary>
        /// Validates whether the given projectNumber and pin are a valid pair.
        /// Does not leak whether the project exists or the pin is wrong.
        /// Returns true if the pair is valid and enabled, false otherwise.
        /// </summary>
        public async Task<bool> ValidatePinAsync(string projectNumber, string pin)
        {
            if (string.IsNullOrWhiteSpace(projectNumber) || string.IsNullOrWhiteSpace(pin))
                return false;

            var normalized = NormalizeProjectNumber(projectNumber);

            var access = await _db.ProjectAccessEntries
                .FirstOrDefaultAsync(pa => pa.ProjectNumber == normalized && pa.IsEnabled);

            if (access == null)
                return false;

            var suppliedHash = HashPin(pin);

            if (!ConstantTimeEquals(access.PinHash, suppliedHash))
                return false;

            try
            {
                access.LastUsedUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Do not fail validation if updating LastUsedUtc fails.
                _logger.LogWarning(ex, "Failed to update LastUsedUtc for ProjectNumber {ProjectNumber}.", normalized);
            }

            return true;
        }

        /// <summary>
        /// Helper for admin tools to set or update a PIN for a project.
        /// Not used from public pages directly.
        /// </summary>
        public async Task<ProjectAccess> SetOrUpdatePinAsync(string projectNumber, string pin)
        {
            if (string.IsNullOrWhiteSpace(projectNumber))
                throw new ArgumentException("Project number is required.", nameof(projectNumber));
            if (string.IsNullOrWhiteSpace(pin))
                throw new ArgumentException("PIN is required.", nameof(pin));

            var normalized = NormalizeProjectNumber(projectNumber);
            var hash = HashPin(pin);

            var existing = await _db.ProjectAccessEntries
                .FirstOrDefaultAsync(pa => pa.ProjectNumber == normalized);

            if (existing == null)
            {
                var newAccess = new ProjectAccess
                {
                    ProjectNumber = normalized,
                    PinHash = hash,
                    IsEnabled = true,
                    CreatedUtc = DateTime.UtcNow
                };

                _db.ProjectAccessEntries.Add(newAccess);
                await _db.SaveChangesAsync();
                return newAccess;
            }
            else
            {
                existing.PinHash = hash;
                existing.IsEnabled = true;
                await _db.SaveChangesAsync();
                return existing;
            }
        }

        private static string NormalizeProjectNumber(string projectNumber)
        {
            var base5 = ProjectNumberHelper.Base5(projectNumber);
            return string.IsNullOrWhiteSpace(base5)
                ? projectNumber.Trim()
                : base5;
        }


        private static byte[] HashPin(string pin)
        {
            if (pin == null)
            {
                throw new ArgumentNullException(nameof(pin));
            }

            // Match SQL Server's NVARCHAR hashing: UTF-16 LE ("Unicode" in .NET)
            using var sha = SHA256.Create();
            var bytes = Encoding.Unicode.GetBytes(pin.Trim());
            return sha.ComputeHash(bytes);
        }

        private static bool ConstantTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;

            var result = 0;
            for (int i = 0; i < a.Length; i++)
            {
                result |= a[i] ^ b[i];
            }
            return result == 0;
        }
    }
}
