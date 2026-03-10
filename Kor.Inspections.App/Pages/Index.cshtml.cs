using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Kor.Inspections.App.Data;
using Kor.Inspections.App.Data.Models;
using Kor.Inspections.App.Options;
using Kor.Inspections.App.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Inspections.App.Pages
{
    public class IndexModel : PageModel
    {
        private readonly InspectionsContext _db;
        private readonly TimeRuleService _timeRules;
        private readonly BookingService _bookingService;
        private readonly ProjectProfileService _projectProfileService;
        private readonly DeltekProjectService _deltekProjectService;
        private readonly ProjectBootstrapVerificationService _projectBootstrapVerificationService;
        private readonly ILogger<IndexModel> _logger;
        private readonly InspectionRulesOptions _inspectionRules;

        public IndexModel(
            InspectionsContext db,
            TimeRuleService timeRules,
            BookingService bookingService,
            ProjectProfileService projectProfileService,
            DeltekProjectService deltekProjectService,
            ProjectBootstrapVerificationService projectBootstrapVerificationService,
            IOptions<InspectionRulesOptions> inspectionRules,
            ILogger<IndexModel> logger)
        {
            _db = db;
            _timeRules = timeRules;
            _bookingService = bookingService;
            _projectProfileService = projectProfileService;
            _deltekProjectService = deltekProjectService;
            _projectBootstrapVerificationService = projectBootstrapVerificationService;
            _inspectionRules = inspectionRules.Value;
            _logger = logger;
        }

        // Honeypot
        [BindProperty]
        public string? CompanyFax { get; set; }

        // ---------------------------
        // Booking fields
        // ---------------------------

        [BindProperty]
        [Required(ErrorMessage = "Kor Job Number is required.")]
        [RegularExpression(@"^\s*\d{5}.*$", ErrorMessage = "Job number must start with 5 digits (e.g., 30844-01).")]
        [StringLength(90)]
        [Display(Name = "Kor Job Number")]
        public string ProjectNumber { get; set; } = string.Empty;

        [BindProperty]
        [StringLength(255)]
        [Display(Name = "Project Address")]
        public string ProjectAddress { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "Site Contact Name is required.")]
        [StringLength(80)]
        [Display(Name = "Site Contact Name")]
        public string ContactName { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "Site Contact Phone is required.")]
        [RegularExpression(
            @"^\s*(\+?1[\s\-\.]?)?(\(?\d{3}\)?[\s\-\.]?)\d{3}[\s\-\.]?\d{4}\s*$",
            ErrorMessage = "Enter a valid 10-digit phone number.")]
        [StringLength(16)]
        [Display(Name = "Site Contact Phone")]
        public string ContactPhone { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "Contact Email is required.")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
        [StringLength(120)]
        [Display(Name = "Contact Email")]
        public string ContactEmail { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "Inspection details are required.")]
        [StringLength(500, MinimumLength = 5, ErrorMessage = "Please provide a bit more detail (at least 5 characters).")]
        [Display(Name = "Inspection Details")]
        public string? Comments { get; set; }

        [BindProperty]
        [Required(ErrorMessage = "Please select a requested date.")]
        [Display(Name = "Requested Date")]
        public DateTime? RequestedDate { get; set; }

        [BindProperty]
        [Required(ErrorMessage = "Please select a requested time.")]
        [Display(Name = "Requested Time")]
        public string? RequestedTime { get; set; }

        public List<string> AvailableTimes { get; private set; } = new();
        public (DateOnly MinDate, DateOnly MaxDate) AllowedDates { get; private set; }

        // Legacy (still safe to keep)
        public ProjectProfileResult? LoadedProfile { get; private set; }

        // Legacy editing (not used for gating Step 2 anymore)
        [BindProperty]
        public int? EditContactId { get; set; }

        [TempData]
        public bool IsNewContactMode { get; set; }

        // ---------------------------
        // Step 2 Gate (MUST BE int?)
        // ---------------------------

        [BindProperty]
        public int? SelectedContactId { get; set; }

        // ---------------- GET ----------------

        public async Task OnGetAsync(string? projectNumber, string? contactEmail, int? selectedContactId)
        {
            AllowedDates = _timeRules.GetAllowedDateRangeUtcNow();

            if (!RequestedDate.HasValue)
                RequestedDate = AllowedDates.MinDate.ToDateTime(TimeOnly.MinValue);

            await LoadAllowedDatesAndTimesAsync();

            if (!string.IsNullOrWhiteSpace(projectNumber))
                ProjectNumber = projectNumber;

            if (!string.IsNullOrWhiteSpace(contactEmail))
                ContactEmail = contactEmail;

            SelectedContactId = selectedContactId;

            // If a selected contact is specified, hydrate Step 2 fields deterministically
            if (!string.IsNullOrWhiteSpace(ProjectNumber) &&
                !string.IsNullOrWhiteSpace(ContactEmail) &&
                SelectedContactId.HasValue)
            {
                LoadedProfile = await _projectProfileService.GetProfileAsync(ProjectNumber, ContactEmail);

                var contact = LoadedProfile?.Contacts.FirstOrDefault(c => c.ContactId == SelectedContactId.Value);
                if (contact != null)
                {
                    ContactName = contact.ContactName;
                    ContactPhone = PhoneNormalizer.Format(contact.ContactPhone);
                    ProjectAddress = contact.ContactAddress ?? string.Empty;
                }
                else
                {
                    // Invalid selection / scope mismatch -> force re-resolution
                    SelectedContactId = null;
                }
            }
        }

        // ---------------------------------------------------------
        // AJAX / MODAL HANDLERS
        // ---------------------------------------------------------

        public sealed class LookupContactsRequest
        {
            public string? ProjectNumber { get; set; }
            public string? Email { get; set; }
        }

        public sealed class SaveContactRequest
        {
            public int? Id { get; set; } // optional for update (int)
            public string? ProjectNumber { get; set; }
            public string? Email { get; set; } // lookup email (domain scope)
            public string? Name { get; set; }
            public string? Phone { get; set; }
            public string? Address { get; set; }
        }

        public sealed class ProjectEmailVerificationRequest
        {
            public string? ProjectNumber { get; set; }
            public string? Email { get; set; }
        }

        public sealed class VerifyProjectEmailCodeRequest
        {
            public string? ProjectNumber { get; set; }
            public string? Email { get; set; }
            public string? Code { get; set; }
        }

        public sealed class ContactDto
        {
            public int Id { get; set; } // int
            public string Name { get; set; } = "";
            public string Phone { get; set; } = "";
            public string Email { get; set; } = "";
            public string? Address { get; set; }
        }

        public sealed class ProjectSuggestionDto
        {
            public string Label { get; set; } = "";
            public string Value { get; set; } = "";
            public string ProjectNumber { get; set; } = "";
            public string? ProjectName { get; set; }
            public string Base5 { get; set; } = "";
            public string? Address { get; set; }
            public string Display { get; set; } = "";
        }

        // ---------------------------
        // Inspections listing DTO
        // ---------------------------

        public sealed class InspectionDto
        {
            public string Id { get; set; } = ""; // safe for int OR Guid BookingId
            public DateTime StartUtc { get; set; }
            public DateTime EndUtc { get; set; }
            public string Status { get; set; } = "";
            public string? AssignedTo { get; set; }
            public string ContactName { get; set; } = "";
            public string ContactEmail { get; set; } = "";
            public string? Address { get; set; }
            public string? Comments { get; set; }
        }

        public sealed class CancelInspectionRequest
        {
            public string? Id { get; set; }
            public string? ProjectNumber { get; set; }
            public string? Email { get; set; }
        }

        public async Task<JsonResult> OnGetProjectSuggestionsAsync(string? term, int limit = 15)
        {
            var q = (term ?? "").Trim();
            if (q.Length < 2)
                return new JsonResult(Array.Empty<ProjectSuggestionDto>());

            var cappedLimit = Math.Clamp(limit, 1, 25);
            var matches = await _deltekProjectService.SearchProjectsAsync(
                q,
                cappedLimit,
                HttpContext.RequestAborted);

            var list = matches
                .Where(m => !string.IsNullOrWhiteSpace(m.ProjectNumber))
                .Select(m =>
                {
                    var value = m.ProjectNumber.Trim();
                    var projectName = string.IsNullOrWhiteSpace(m.ProjectName) ? null : m.ProjectName.Trim();
                    var labelParts = new List<string> { value };
                    if (!string.IsNullOrWhiteSpace(projectName))
                        labelParts.Add(projectName);

                    var label = string.Join(" - ", labelParts);

                    return new ProjectSuggestionDto
                    {
                        Label = label,
                        Value = value,
                        ProjectNumber = value,
                        ProjectName = projectName,
                        Base5 = ProjectNumberHelper.Base5(value),
                        Display = label
                    };
                })
                .ToList();

            if (_logger.IsEnabled(LogLevel.Information))
            {
                var matchNames = list
                    .Select(m => $"{m.ProjectNumber}:{m.ProjectName ?? "<null>"}")
                    .ToList();
                _logger.LogInformation(
                    "Project suggestions for {Term} -> {Matches}",
                    q,
                    matchNames);
            }

            return new JsonResult(list);
        }

        public async Task<JsonResult> OnPostLookupContactsAsync([FromBody] LookupContactsRequest req)
        {
            var project = (req.ProjectNumber ?? "").Trim();
            var email = (req.Email ?? "").Trim();

            if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(email))
                return new JsonResult(Array.Empty<ContactDto>());

            var canAccess = await _projectBootstrapVerificationService
                .EnsureVerifiedForProjectAccessAsync(project, email, HttpContext.RequestAborted);
            if (!canAccess)
            {
                Response.StatusCode = 403;
                return new JsonResult(new { error = "Please verify your email before accessing project contacts." });
            }

            var profile = await _projectProfileService.GetProfileAsync(project, email);

            var list = (profile?.Contacts ?? new List<ProjectContact>())
                .Select(c => new ContactDto
                {
                    Id = c.ContactId,
                    Name = c.ContactName,
                    Phone = PhoneNormalizer.Format(c.ContactPhone),
                    Email = c.ContactEmail,
                    Address = c.ContactAddress
                })
                .OrderBy(c => c.Name)
                .ToList();

            return new JsonResult(list);
        }

        // ---------------------------------------------------------
        // Lookup inspections by project + email-domain scope
        // ---------------------------------------------------------

        public async Task<JsonResult> OnPostLookupInspectionsAsync([FromBody] LookupContactsRequest req)
        {
            var projectRaw = (req.ProjectNumber ?? "").Trim();
            var emailRaw = (req.Email ?? "").Trim();

            if (string.IsNullOrWhiteSpace(projectRaw) || string.IsNullOrWhiteSpace(emailRaw))
                return new JsonResult(Array.Empty<InspectionDto>());

            var canAccess = await _projectBootstrapVerificationService
                .EnsureVerifiedForProjectAccessAsync(projectRaw, emailRaw, HttpContext.RequestAborted);

            if (!canAccess)
            {
                Response.StatusCode = 403;
                return new JsonResult(new { error = "Please verify your email before viewing inspections." });
            }

            var at = emailRaw.IndexOf('@');
            if (at <= 0 || at >= emailRaw.Length - 1)
                return new JsonResult(Array.Empty<InspectionDto>());

            var domain = emailRaw[(at + 1)..].Trim().ToLowerInvariant();
            var base5 = ProjectNumberHelper.Base5(projectRaw);

            if (string.IsNullOrWhiteSpace(base5) || string.IsNullOrWhiteSpace(domain))
                return new JsonResult(Array.Empty<InspectionDto>());

            var domainSuffix = "@" + domain;

            var bookings = await _db.Bookings
                .AsNoTracking()
                .Where(b => b.ProjectNumber != null && b.ProjectNumber.StartsWith(base5))
                .Where(b => b.ContactEmail != null && EF.Functions.Like(b.ContactEmail, "%" + domainSuffix))
                .OrderByDescending(b => b.StartUtc)
                .ToListAsync();

            var inspectorsByEmail = await _db.Inspectors
                .AsNoTracking()
                .Where(i => !string.IsNullOrWhiteSpace(i.Email))
                .GroupBy(i => i.Email)
                .Select(g => new { Email = g.Key, DisplayName = g.First().DisplayName })
                .ToDictionaryAsync(x => x.Email, x => x.DisplayName, StringComparer.OrdinalIgnoreCase);

            var list = bookings
                .Select(b => new InspectionDto
                {
                    Id = b.BookingId.ToString(),
                    StartUtc = b.StartUtc,
                    EndUtc = b.EndUtc,
                    Status = b.Status,
                    AssignedTo = ResolveAssignedToDisplay(b.AssignedTo, inspectorsByEmail),
                    ContactName = b.ContactName,
                    ContactEmail = b.ContactEmail,
                    Address = b.ProjectAddress,
                    Comments = b.Comments
                })
                .ToList();

            return new JsonResult(list);
        }

        public async Task<JsonResult> OnPostCancelInspectionAsync([FromBody] CancelInspectionRequest req)
        {
            var projectRaw = (req.ProjectNumber ?? "").Trim();
            var emailRaw = (req.Email ?? "").Trim();
            var idRaw = (req.Id ?? "").Trim();

            if (string.IsNullOrWhiteSpace(projectRaw) || string.IsNullOrWhiteSpace(emailRaw) || string.IsNullOrWhiteSpace(idRaw))
            {
                Response.StatusCode = 400;
                return new JsonResult(new { error = "Project number, email, and inspection id are required." });
            }

            var canAccess = await _projectBootstrapVerificationService
                .EnsureVerifiedForProjectAccessAsync(projectRaw, emailRaw, HttpContext.RequestAborted);
            if (!canAccess)
            {
                Response.StatusCode = 403;
                return new JsonResult(new { error = "Please verify your email before cancelling an inspection." });
            }

            if (!Guid.TryParse(idRaw, out var bookingId))
            {
                Response.StatusCode = 400;
                return new JsonResult(new { error = "Invalid inspection id." });
            }

            var at = emailRaw.IndexOf('@');
            if (at <= 0 || at >= emailRaw.Length - 1)
            {
                Response.StatusCode = 400;
                return new JsonResult(new { error = "Invalid email." });
            }

            var domain = emailRaw[(at + 1)..].Trim().ToLowerInvariant();
            var base5 = ProjectNumberHelper.Base5(projectRaw);
            var domainSuffix = "@" + domain;

            if (string.IsNullOrWhiteSpace(base5) || string.IsNullOrWhiteSpace(domain))
            {
                Response.StatusCode = 400;
                return new JsonResult(new { error = "Invalid project scope." });
            }

            var booking = await _db.Bookings
                .FirstOrDefaultAsync(b =>
                    b.BookingId == bookingId &&
                    b.ProjectNumber != null &&
                    b.ProjectNumber.StartsWith(base5) &&
                    b.ContactEmail != null &&
                    EF.Functions.Like(b.ContactEmail, "%" + domainSuffix));

            if (booking == null)
            {
                Response.StatusCode = 404;
                return new JsonResult(new { error = "Inspection not found." });
            }

            if (string.Equals(booking.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                return new JsonResult(new { ok = true });

            if (string.Equals(booking.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                Response.StatusCode = 400;
                return new JsonResult(new { error = "Completed inspections cannot be cancelled." });
            }

            if (!_timeRules.IsCancellationAllowed(booking.StartUtc))
            {
                Response.StatusCode = 400;
                return new JsonResult(new { error = "Cancellation window has closed for this inspection." });
            }

            booking.Status = "Cancelled";
            _db.BookingActions.Add(new BookingAction
            {
                BookingId = booking.BookingId,
                ActionType = "Cancelled",
                PerformedBy = emailRaw,
                ActionUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
            await _bookingService.SendCancellationEmailsAsync(booking);

            return new JsonResult(new { ok = true });
        }

        private static string? ResolveAssignedToDisplay(
            string? assignedTo,
            IReadOnlyDictionary<string, string> inspectorsByEmail)
        {
            if (string.IsNullOrWhiteSpace(assignedTo))
                return assignedTo;

            return inspectorsByEmail.TryGetValue(assignedTo, out var displayName)
                ? displayName
                : assignedTo;
        }

        [EnableRateLimiting("verification")]
        public async Task<JsonResult> OnPostProjectEmailVerificationStatusAsync(
            [FromBody] ProjectEmailVerificationRequest req)
        {
            var project = (req.ProjectNumber ?? "").Trim();
            var email = (req.Email ?? "").Trim();

            if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(email))
                return new JsonResult(new { requiresVerification = false, isVerified = false });

            var status = await _projectBootstrapVerificationService.GetStatusAsync(
                project,
                email,
                HttpContext.RequestAborted);

            return new JsonResult(new
            {
                requiresVerification = status.RequiresVerification,
                isVerified = status.IsVerified
            });
        }

        [EnableRateLimiting("verification")]
        public async Task<JsonResult> OnPostSendProjectEmailCodeAsync(
            [FromBody] ProjectEmailVerificationRequest req)
        {
            var project = (req.ProjectNumber ?? "").Trim();
            var email = (req.Email ?? "").Trim();

            if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(email))
            {
                Response.StatusCode = 400;
                return new JsonResult(new { error = "Project number and email are required." });
            }

            var sent = await _projectBootstrapVerificationService.SendCodeAsync(
                project,
                email,
                HttpContext.RequestAborted);

            if (!sent)
            {
                Response.StatusCode = 500;
                return new JsonResult(new { error = "Unable to send verification code. Please try again." });
            }

            return new JsonResult(new { ok = true });
        }

        [EnableRateLimiting("verification")]
        public async Task<JsonResult> OnPostVerifyProjectEmailCodeAsync(
            [FromBody] VerifyProjectEmailCodeRequest req)
        {
            var project = (req.ProjectNumber ?? "").Trim();
            var email = (req.Email ?? "").Trim();
            var code = (req.Code ?? "").Trim();

            if (string.IsNullOrWhiteSpace(project) ||
                string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(code))
            {
                Response.StatusCode = 400;
                return new JsonResult(new { error = "Project number, email, and code are required." });
            }

            var verified = await _projectBootstrapVerificationService.VerifyCodeAsync(
                project,
                email,
                code,
                HttpContext.RequestAborted);

            if (!verified)
            {
                Response.StatusCode = 400;
                return new JsonResult(new { error = "Invalid or expired verification code." });
            }

            return new JsonResult(new { ok = true });
        }

        [EnableRateLimiting("contactMutation")]
        public async Task<JsonResult> OnPostSaveContactAjaxAsync([FromBody] SaveContactRequest req)
        {
            var project = (req.ProjectNumber ?? "").Trim();
            var requesterEmail = string.IsNullOrWhiteSpace(ContactEmail)
                ? (req.Email ?? "").Trim()
                : ContactEmail.Trim();
            var contactEmail = (req.Email ?? "").Trim();
            var name = (req.Name ?? "").Trim();
            var phone = (req.Phone ?? "").Trim();
            var address = (req.Address ?? "").Trim();

            if (string.IsNullOrWhiteSpace(project) ||
                string.IsNullOrWhiteSpace(requesterEmail) ||
                string.IsNullOrWhiteSpace(contactEmail) ||
                string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(phone))
            {
                Response.StatusCode = 400;
                return new JsonResult(new { error = "Project, email, name, and phone are required." });
            }

            var canAccess = await _projectBootstrapVerificationService
                .EnsureVerifiedForProjectAccessAsync(project, requesterEmail, HttpContext.RequestAborted);
            if (!canAccess)
            {
                Response.StatusCode = 403;
                return new JsonResult(new { error = "Please verify your email before saving project contacts." });
            }

            ProjectContact saved;
            try
            {
                saved = await _projectProfileService.AddOrUpdateContactAsync(
                    req.Id,
                    project,
                    requesterEmail,
                    name,
                    PhoneNormalizer.Normalize(phone),
                    contactEmail,
                    string.IsNullOrWhiteSpace(address) ? null : address);
            }
            catch (InvalidOperationException)
            {
                Response.StatusCode = 409;
                return new JsonResult(new { error = "This contact already exists or was updated by another user. Please refresh and try again." });
            }

            var dto = new ContactDto
            {
                Id = saved.ContactId,
                Name = saved.ContactName,
                Phone = PhoneNormalizer.Format(saved.ContactPhone),
                Email = saved.ContactEmail,
                Address = saved.ContactAddress
            };

            return new JsonResult(dto);
        }

        [EnableRateLimiting("contactMutation")]
        public async Task<JsonResult> OnPostSelectContactAsync(int id)
        {
            if (string.IsNullOrWhiteSpace(ProjectNumber) || string.IsNullOrWhiteSpace(ContactEmail))
            {
                Response.StatusCode = 400;
                return new JsonResult(new { error = "Project number and email are required." });
            }

            var canAccess = await _projectBootstrapVerificationService
                .EnsureVerifiedForProjectAccessAsync(ProjectNumber, ContactEmail, HttpContext.RequestAborted);
            if (!canAccess)
            {
                Response.StatusCode = 403;
                return new JsonResult(new { error = "Please verify your email before selecting a contact." });
            }

            var profile = await _projectProfileService.GetProfileAsync(ProjectNumber, ContactEmail);
            var contact = profile?.Contacts.FirstOrDefault(c => c.ContactId == id);

            if (contact == null)
            {
                Response.StatusCode = 404;
                return new JsonResult(new { error = "Contact not found." });
            }

            SelectedContactId = contact.ContactId;

            ContactName = contact.ContactName;
            ContactPhone = PhoneNormalizer.Format(contact.ContactPhone);
            ProjectAddress = contact.ContactAddress ?? string.Empty;

            var dto = new ContactDto
            {
                Id = contact.ContactId,
                Name = contact.ContactName,
                Phone = PhoneNormalizer.Format(contact.ContactPhone),
                Email = contact.ContactEmail,
                Address = contact.ContactAddress
            };

            return new JsonResult(dto);
        }

        [EnableRateLimiting("contactMutation")]
        public async Task<IActionResult> OnPostDeleteContactAsync(int id)
        {
            if (string.IsNullOrWhiteSpace(ProjectNumber) || string.IsNullOrWhiteSpace(ContactEmail))
                return new JsonResult(new { error = "Project number and email are required." }) { StatusCode = 400 };

            var canAccess = await _projectBootstrapVerificationService
                .EnsureVerifiedForProjectAccessAsync(ProjectNumber, ContactEmail, HttpContext.RequestAborted);
            if (!canAccess)
                return new JsonResult(new { error = "Please verify your email before deleting a contact." }) { StatusCode = 403 };

            await _projectProfileService.DeleteContactAsync(
                id,
                ProjectNumber,
                ContactEmail);

            if (SelectedContactId == id)
                SelectedContactId = null;

            return new JsonResult(new { ok = true });
        }

        public async Task<IActionResult> OnPostRefreshTimesAsync()
        {
            await EnsureDateStateAsync();
            ModelState.Clear();
            return Page();
        }

        // ---------------- BOOK ----------------

        [EnableRateLimiting("booking")]
        public async Task<IActionResult> OnPostBookAsync()
        {
            if (!string.IsNullOrWhiteSpace(CompanyFax))
                return RedirectToPage("Confirm");

            await LoadAllowedDatesAndTimesAsync();

            var canAccess = await _projectBootstrapVerificationService
                .EnsureVerifiedForProjectAccessAsync(ProjectNumber, ContactEmail, HttpContext.RequestAborted);
            if (!canAccess)
            {
                ModelState.AddModelError(string.Empty, "Please verify your email before booking.");
                return Page();
            }

            if (!SelectedContactId.HasValue)
            {
                ModelState.AddModelError(string.Empty, "Please select or create a saved contact before booking.");
                return Page();
            }

            if (!ModelState.IsValid)
                return Page();

            var profile = await _projectProfileService.GetProfileAsync(ProjectNumber, ContactEmail);
            var contact = profile?.Contacts.FirstOrDefault(c => c.ContactId == SelectedContactId.Value);

            if (contact == null)
            {
                SelectedContactId = null;
                ModelState.AddModelError(string.Empty, "Selected contact is invalid. Please re-select a contact.");
                return Page();
            }

            var requestDateOnly = DateOnly.FromDateTime(RequestedDate!.Value);
            var (minDate, maxDate) = _timeRules.GetAllowedDateRangeUtcNow();
            if (requestDateOnly < minDate || requestDateOnly > maxDate)
            {
                await LoadAllowedDatesAndTimesAsync();
                ModelState.AddModelError(string.Empty, "Selected date is outside the allowed booking window.");
                return Page();
            }

            DateTime startUtc;
            DateTime endUtc;
            string? timePreference = null;

            if (RequestedTime == "AM" || RequestedTime == "PM")
            {
                timePreference = RequestedTime;

                var anchorTime = RequestedTime == "AM"
                    ? new TimeOnly(8, 0)
                    : new TimeOnly(12, 0);

                startUtc = _timeRules.ConvertLocalToUtc(requestDateOnly, anchorTime);
                endUtc = startUtc.AddHours(4);
            }
            else
            {
                if (!TryParseTime(RequestedTime!, out var timeOnly))
                {
                    ModelState.AddModelError(string.Empty, "Invalid time selected.");
                    return Page();
                }

                var existingForDate = await GetExistingBookingsForLocalDateAsync(requestDateOnly);

                var availableSlots = _timeRules
                    .GetAvailableSlotsForDate(requestDateOnly, existingForDate)
                    .ToList();

                if (!availableSlots.Contains(timeOnly))
                {
                    ModelState.AddModelError(string.Empty,
                        "Selected time is no longer available. Please choose another time.");
                    return Page();
                }

                startUtc = _timeRules.ConvertLocalToUtc(requestDateOnly, timeOnly);
                endUtc = startUtc.AddMinutes(_inspectionRules.DefaultDurationMinutes);
            }

            var submittedProjectNumber = ProjectNumber.Trim();
            var submittedContactEmail = contact.ContactEmail.Trim();
            var duplicateCutoffUtc = DateTime.UtcNow.AddMinutes(-2);

            var duplicateExists = await _db.Bookings
                .AsNoTracking()
                .AnyAsync(b =>
                    b.ProjectNumber == submittedProjectNumber &&
                    b.ContactEmail == submittedContactEmail &&
                    b.StartUtc == startUtc &&
                    b.Status != "Cancelled" &&
                    b.CreatedUtc >= duplicateCutoffUtc);

            if (duplicateExists)
            {
                _logger.LogWarning(
                    "Duplicate booking submission blocked for {ProjectNumber} at {StartUtc} ({ContactEmail}).",
                    submittedProjectNumber,
                    startUtc,
                    submittedContactEmail);
                await LoadAllowedDatesAndTimesAsync();
                ModelState.AddModelError(string.Empty, "This booking was already submitted. Please check your confirmation email.");
                return Page();
            }

            Booking booking;
            try
            {
                booking = await _bookingService.CreateBookingAsync(
                    submittedProjectNumber,
                    string.IsNullOrWhiteSpace(contact.ContactAddress)
                        ? (string.IsNullOrWhiteSpace(ProjectAddress) ? $"{ProjectNumber.Trim()} Project Address" : ProjectAddress.Trim())
                        : contact.ContactAddress.Trim(),
                    contact.ContactName.Trim(),
                    PhoneNormalizer.Normalize(contact.ContactPhone),
                    submittedContactEmail,
                    string.IsNullOrWhiteSpace(Comments) ? null : Comments.Trim(),
                    startUtc,
                    endUtc,
                    timePreference);
            }
            catch (BookingSlotUnavailableException)
            {
                await LoadAllowedDatesAndTimesAsync();
                ModelState.AddModelError(string.Empty,
                    "Selected time is no longer available. Please choose another time.");
                return Page();
            }

            return RedirectToPage("Confirm", new { id = booking.BookingId });
        }

        // ---------------- HELPERS ----------------

        private async Task EnsureDateStateAsync()
        {
            AllowedDates = _timeRules.GetAllowedDateRangeUtcNow();

            if (!RequestedDate.HasValue)
                RequestedDate = AllowedDates.MinDate.ToDateTime(TimeOnly.MinValue);

            await LoadAllowedDatesAndTimesAsync();
        }

        private async Task LoadAllowedDatesAndTimesAsync()
        {
            AllowedDates = _timeRules.GetAllowedDateRangeUtcNow();
            AvailableTimes = new();

            if (!RequestedDate.HasValue)
                return;

            var dateOnly = DateOnly.FromDateTime(RequestedDate.Value);
            var existingForDate = await GetExistingBookingsForLocalDateAsync(dateOnly);

            var slots = _timeRules.GetAvailableSlotsForDate(dateOnly, existingForDate);

            AvailableTimes = slots.Select(t => t.ToString("HH:mm")).ToList();
        }

        private async Task<List<Booking>> GetExistingBookingsForLocalDateAsync(DateOnly localDate)
        {
            var localStart = localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            var localEnd = localDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

            var utcStart = TimeZoneInfo.ConvertTimeToUtc(localStart, _timeRules.TimeZone);
            var utcEnd = TimeZoneInfo.ConvertTimeToUtc(localEnd, _timeRules.TimeZone);

            return await _db.Bookings
                .Where(b => b.Status != "Cancelled")
                .Where(b => b.StartUtc >= utcStart && b.StartUtc < utcEnd)
                .ToListAsync();
        }

        private static bool TryParseTime(string input, out TimeOnly time)
        {
            time = default;

            var parts = input.Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) return false;

            if (!int.TryParse(parts[0], out var hour)) return false;
            if (!int.TryParse(parts[1], out var minute)) return false;

            if (hour is < 0 or > 23) return false;
            if (minute is < 0 or > 59) return false;

            time = new TimeOnly(hour, minute);
            return true;
        }

    }
}
