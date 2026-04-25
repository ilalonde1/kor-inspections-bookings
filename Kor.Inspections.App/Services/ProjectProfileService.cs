using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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

        public async Task<ProjectProfileResult> GetProfileAsync(
            string projectNumber,
            string contactEmail,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(contactEmail))
                return ProjectProfileResult.Empty();

            contactEmail = contactEmail.Trim().ToLowerInvariant();
            var domain = EmailHelper.GetDomain(contactEmail);

            if (string.IsNullOrWhiteSpace(domain))
                return ProjectProfileResult.Empty();

            projectNumber = NormalizeProject(projectNumber);
            List<ProjectContact> contacts = await _db.ProjectContacts
                .AsNoTracking()
                .Where(c =>
                    c.ProjectNumber == projectNumber &&
                    c.EmailDomain == domain &&
                    !c.IsDeleted)
                .OrderBy(c => c.ContactName)
                .ToListAsync(ct);

            return new ProjectProfileResult
            {
                ProjectNumber = projectNumber,
                EmailDomain = domain,
                Contacts = contacts
            };
        }

        public async Task<ProjectContact> AddOrUpdateContactAsync(
            int? contactId,
            string projectNumber,
            string contactEmail,
            string name,
            string phone,
            string email,
            string? address,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(email))
                throw new InvalidContactEmailException("Contact email is required.");

            email = email.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(EmailHelper.GetDomain(email)))
                throw new InvalidContactEmailException("Invalid contact email.");

            contactEmail = (contactEmail ?? string.Empty).Trim().ToLowerInvariant();
            var domain = EmailHelper.GetDomain(contactEmail);

            if (string.IsNullOrWhiteSpace(domain))
                throw new InvalidContactEmailException("Invalid contact email.");

            projectNumber = NormalizeProject(projectNumber);

            ProjectContact contact;

            if (contactId.HasValue)
            {
                contact = await _db.ProjectContacts.FirstOrDefaultAsync(c =>
                    c.ContactId == contactId.Value &&
                    c.ProjectNumber == projectNumber &&
                    c.EmailDomain == domain &&
                    !c.IsDeleted,
                    ct)
                    ?? throw new ContactNotFoundException("Contact not found.");
            }
            else
            {
                contact = await _db.ProjectContacts.FirstOrDefaultAsync(c =>
                    c.ProjectNumber == projectNumber &&
                    c.EmailDomain == domain &&
                    c.ContactEmail == email &&
                    !c.IsDeleted,
                    ct)
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
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
                when (DbUpdateExceptionClassifier.IsNamedUniqueConstraintViolation(
                    ex,
                    "IX_ProjectContacts"))
            {
                throw new ContactAlreadyExistsException(
                    "A contact with this email already exists for this project.");
            }

            return contact;
        }

        public async Task DeleteContactAsync(
            int contactId,
            string projectNumber,
            string contactEmail,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(contactEmail))
                return;

            contactEmail = contactEmail.Trim().ToLowerInvariant();
            var domain = EmailHelper.GetDomain(contactEmail);

            if (string.IsNullOrWhiteSpace(domain))
                return;

            projectNumber = NormalizeProject(projectNumber);

            ProjectContact? contact = await _db.ProjectContacts.FirstOrDefaultAsync(c =>
                c.ContactId == contactId &&
                c.ProjectNumber == projectNumber &&
                c.EmailDomain == domain &&
                !c.IsDeleted,
                ct);

            if (contact == null)
                return;

            contact.IsDeleted = true;
            contact.UpdatedUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
        }

        private static string NormalizeProject(string projectNumber)
        {
            var base5 = ProjectNumberHelper.Base5(projectNumber);

            return string.IsNullOrWhiteSpace(base5)
                ? (projectNumber ?? "").Trim()
                : base5;
        }
    }

    public class ProjectProfileResult
    {
        public string ProjectNumber { get; set; } = "";
        public string EmailDomain { get; set; } = "";
        public List<ProjectContact> Contacts { get; set; } = new();

        public static ProjectProfileResult Empty() => new();
    }

    public class ContactNotFoundException : Exception
    {
        public ContactNotFoundException(string message) : base(message) { }
    }

    public class InvalidContactEmailException : Exception
    {
        public InvalidContactEmailException(string message) : base(message) { }
    }

    public class ContactAlreadyExistsException : Exception
    {
        public ContactAlreadyExistsException(string message) : base(message) { }
    }
}
