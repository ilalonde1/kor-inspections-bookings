using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kor.Inspections.App.Data;
using Kor.Inspections.App.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Kor.Inspections.App.Services
{
    public class ProjectProfileService
    {
        private readonly InspectionsContext _db;

        public ProjectProfileService(InspectionsContext db)
        {
            _db = db;
        }

        // --------------------------------------------------
        // Public API
        // --------------------------------------------------

        public async Task<bool> HasAnyActiveContactsForProjectAsync(string projectNumber)
        {
            projectNumber = NormalizeProject(projectNumber);
            if (string.IsNullOrWhiteSpace(projectNumber))
                return false;

            return await _db.ProjectContacts
                .AsNoTracking()
                .AnyAsync(c => c.ProjectNumber == projectNumber && !c.IsDeleted);
        }

        public async Task<bool> HasActiveContactForProjectEmailAsync(string projectNumber, string email)
        {
            projectNumber = NormalizeProject(projectNumber);
            if (string.IsNullOrWhiteSpace(projectNumber))
                return false;

            email = (email ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(email))
                return false;

            return await _db.ProjectContacts
                .AsNoTracking()
                .AnyAsync(c =>
                    c.ProjectNumber == projectNumber &&
                    c.ContactEmail == email &&
                    !c.IsDeleted);
        }

        public async Task<ProjectProfileResult> GetProfileAsync(
            string projectNumber,
            string contactEmail)
        {
            if (string.IsNullOrWhiteSpace(contactEmail))
                return ProjectProfileResult.Empty();

            contactEmail = contactEmail.Trim().ToLowerInvariant();
            var domain = GetEmailDomain(contactEmail);

            if (string.IsNullOrWhiteSpace(domain))
                return ProjectProfileResult.Empty();

            projectNumber = NormalizeProject(projectNumber);

            ProjectDefault? defaults = await _db.ProjectDefaults
                .AsNoTracking()
                .FirstOrDefaultAsync(d =>
                    d.ProjectNumber == projectNumber &&
                    d.EmailDomain == domain);

            List<ProjectContact> contacts = await _db.ProjectContacts
                .AsNoTracking()
                .Where(c =>
                    c.ProjectNumber == projectNumber &&
                    c.EmailDomain == domain &&
                    !c.IsDeleted)
                .OrderBy(c => c.ContactName)
                .ToListAsync();


            return new ProjectProfileResult
            {
                ProjectNumber = projectNumber,
                EmailDomain = domain,
                DefaultAddress = defaults?.DefaultAddress,
                Contacts = contacts
            };
        }

        // Legacy compatibility — safe to keep
        public async Task SaveDefaultAddressAsync(
            string projectNumber,
            string contactEmail,
            string address)
        {
            if (string.IsNullOrWhiteSpace(contactEmail))
                return;

            contactEmail = contactEmail.Trim().ToLowerInvariant();
            var domain = GetEmailDomain(contactEmail);

            if (string.IsNullOrWhiteSpace(domain))
                return;

            projectNumber = NormalizeProject(projectNumber);

            ProjectDefault row = await GetOrCreateDefaultsAsync(projectNumber, domain);

            row.DefaultAddress = string.IsNullOrWhiteSpace(address)
                ? null
                : address.Trim();

            row.UpdatedUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync();
        }

        public async Task<ProjectContact> AddOrUpdateContactAsync(
            int? contactId,
            string projectNumber,
            string contactEmail,
            string name,
            string phone,
            string email,
            string? address)
        {
            if (string.IsNullOrWhiteSpace(email))
                throw new ArgumentException("Contact email is required.", nameof(email));

            email = email.Trim().ToLowerInvariant();
            contactEmail = (contactEmail ?? string.Empty).Trim().ToLowerInvariant();
            var domain = GetEmailDomain(contactEmail);

            if (string.IsNullOrWhiteSpace(domain))
                throw new ArgumentException("Invalid contact email.", nameof(email));

            projectNumber = NormalizeProject(projectNumber);

            ProjectContact contact;

            // Explicit edit by ID
            if (contactId.HasValue)
            {
                contact = await _db.ProjectContacts.FirstOrDefaultAsync(c =>
                    c.ContactId == contactId.Value &&
                    c.ProjectNumber == projectNumber &&
                    c.EmailDomain == domain &&
                    !c.IsDeleted)
                    ?? throw new InvalidOperationException("Contact not found.");
            }
            else
            {
                // Email defines uniqueness
                contact = await _db.ProjectContacts.FirstOrDefaultAsync(c =>
                    c.ProjectNumber == projectNumber &&
                    c.EmailDomain == domain &&
                    c.ContactEmail == email &&
                    !c.IsDeleted)
                    ?? new ProjectContact
                    {
                        ProjectNumber = projectNumber,
                        EmailDomain = domain,
                        ContactEmail = email
                    };

                if (contact.ContactId == 0)
                    _db.ProjectContacts.Add(contact);
            }

            if (!string.IsNullOrWhiteSpace(name))
                contact.ContactName = name.Trim();

            if (!string.IsNullOrWhiteSpace(phone))
                contact.ContactPhone = phone.Trim();

            if (address != null)
                contact.ContactAddress = string.IsNullOrWhiteSpace(address)
                    ? null
                    : address.Trim();


            contact.UpdatedUtc = DateTime.UtcNow;

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
                when (ex.InnerException?.Message.Contains("IX_ProjectContacts") == true)
            {
                throw new InvalidOperationException(
                    "A contact with this email already exists for this project.");
            }

            return contact;
        }

        public async Task DeleteContactAsync(
            int contactId,
            string projectNumber,
            string contactEmail)
        {
            if (string.IsNullOrWhiteSpace(contactEmail))
                return;

            contactEmail = contactEmail.Trim().ToLowerInvariant();
            var domain = GetEmailDomain(contactEmail);

            if (string.IsNullOrWhiteSpace(domain))
                return;

            projectNumber = NormalizeProject(projectNumber);

            ProjectContact? contact = await _db.ProjectContacts.FirstOrDefaultAsync(c =>
                c.ContactId == contactId &&
                c.ProjectNumber == projectNumber &&
                c.EmailDomain == domain &&
                !c.IsDeleted);

            if (contact == null)
                return;

            contact.IsDeleted = true;
            contact.UpdatedUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync();
        }

        // --------------------------------------------------
        // Internal helpers
        // --------------------------------------------------

        private async Task<ProjectDefault> GetOrCreateDefaultsAsync(
            string projectNumber,
            string domain)
        {
            ProjectDefault? row = await _db.ProjectDefaults.FirstOrDefaultAsync(d =>
                d.ProjectNumber == projectNumber &&
                d.EmailDomain == domain);

            if (row != null)
                return row;

            row = new ProjectDefault
            {
                ProjectNumber = projectNumber,
                EmailDomain = domain,
                UpdatedUtc = DateTime.UtcNow
            };

            _db.ProjectDefaults.Add(row);
            await _db.SaveChangesAsync();

            return row;
        }

        private static string GetEmailDomain(string email)
        {
            var at = email.LastIndexOf('@');

            return (at > 0 && at < email.Length - 1)
                ? email[(at + 1)..]
                : "";
        }

        private static string NormalizeProject(string projectNumber)
        {
            var base5 = ProjectNumberHelper.Base5(projectNumber);

            return string.IsNullOrWhiteSpace(base5)
                ? (projectNumber ?? "").Trim()
                : base5;
        }
    }

    // --------------------------------------------------
    // Result DTO returned to booking page
    // --------------------------------------------------

    public class ProjectProfileResult
    {
        public string ProjectNumber { get; set; } = "";
        public string EmailDomain { get; set; } = "";
        public string? DefaultAddress { get; set; }
        public List<ProjectContact> Contacts { get; set; } = new();

        public static ProjectProfileResult Empty() => new();
    }
}
