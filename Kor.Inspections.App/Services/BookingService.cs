// CODEX TEST  verified update
using System;
using System.Linq;
using System.Data;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Kor.Inspections.App.Data;
using Kor.Inspections.App.Data.Models;
using Kor.Inspections.App.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Inspections.App.Services
{
    public sealed class BookingSlotUnavailableException : Exception
    {
        public BookingSlotUnavailableException(string message) : base(message) { }
    }

    public class BookingService
    {
        private readonly InspectionsContext _db;
        private readonly NotificationOptions _notificationOptions;
        private readonly AppOptions _appOptions;
        private readonly ILogger<BookingService> _logger;
        private readonly TimeRuleService _timeRules;
        private readonly GraphMailService _graphMail;
        private readonly InspectionRulesOptions _inspectionRules;

        public BookingService(
            InspectionsContext db,
            IOptions<NotificationOptions> notificationOptions,
            ILogger<BookingService> logger,
            TimeRuleService timeRules,
            GraphMailService graphMail,
            IOptions<InspectionRulesOptions> inspectionRulesOptions,
            IOptions<AppOptions> appOptions)
        {
            _db = db;
            _notificationOptions = notificationOptions.Value;
            _logger = logger;
            _timeRules = timeRules;
            _graphMail = graphMail;
            _inspectionRules = inspectionRulesOptions.Value;
            _appOptions = appOptions.Value;
        }

        public Task<Booking?> GetBookingAsync(Guid id)
        {
            return _db.Bookings.FirstOrDefaultAsync(b => b.BookingId == id);
        }

        public Task<Booking?> GetBookingByCancelTokenAsync(Guid token)
        {
            return _db.Bookings.FirstOrDefaultAsync(b => b.CancelToken == token);
        }

        private Task<Inspector?> GetAssignedInspectorAsync(string? assignedTo, bool requireEnabled)
        {
            if (string.IsNullOrWhiteSpace(assignedTo))
                return Task.FromResult<Inspector?>(null);

            var query = _db.Inspectors.AsQueryable();

            if (requireEnabled)
                query = query.Where(i => i.Enabled);

            return query.FirstOrDefaultAsync(i =>
                i.Email == assignedTo ||
                i.DisplayName == assignedTo);
        }

        private void RecordAction(
            Guid bookingId,
            string actionType,
            string? performedBy,
            string? notes = null)
        {
            _db.BookingActions.Add(new BookingAction
            {
                BookingId = bookingId,
                ActionType = actionType,
                PerformedBy = performedBy,
                Notes = notes,
                ActionUtc = DateTime.UtcNow
            });
        }

        // --------------------------------------------------
        // Booking creation
        // --------------------------------------------------
        public async Task<Booking> CreateBookingAsync(
            string projectNumber,
            string projectAddress,
            string contactName,
            string contactPhone,
            string contactEmail,
            string? comments,
            DateTime startUtc,
            DateTime endUtc,
            string? timePreference)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            // Authoritative availability check inside a serializable transaction.
            var tz = _timeRules.TimeZone;
            var slotStartLocal = TimeZoneInfo.ConvertTimeFromUtc(startUtc, tz);
            var slotEndLocal = TimeZoneInfo.ConvertTimeFromUtc(endUtc, tz);
            var padding = TimeSpan.FromMinutes(_inspectionRules.TravelPaddingMinutes);

            var checkStartLocal = DateTime.SpecifyKind(slotStartLocal - padding, DateTimeKind.Unspecified);
            var checkEndLocal = DateTime.SpecifyKind(slotEndLocal + padding, DateTimeKind.Unspecified);

            var checkStartUtc = TimeZoneInfo.ConvertTimeToUtc(checkStartLocal, tz);
            var checkEndUtc = TimeZoneInfo.ConvertTimeToUtc(checkEndLocal, tz);

            var overlapCount = await _db.Bookings
                .Where(b =>
                    b.Status != "Cancelled" &&
                    b.StartUtc < checkEndUtc &&
                    b.EndUtc > checkStartUtc)
                .CountAsync();

            var maxBookingsPerSlot = Math.Max(1, _inspectionRules.MaxBookingsPerSlot);
            if (overlapCount >= maxBookingsPerSlot)
            {
                throw new BookingSlotUnavailableException(
                    "Selected time is no longer available.");
            }

            var booking = new Booking
            {
                ProjectNumber = projectNumber,
                ProjectAddress = projectAddress,
                ContactName = contactName,
                ContactPhone = contactPhone,
                ContactEmail = contactEmail,
                Comments = comments,
                StartUtc = startUtc,
                EndUtc = endUtc,
                TimePreference = timePreference,
                Status = "Unassigned",
                CreatedUtc = DateTime.UtcNow
            };

            _db.Bookings.Add(booking);
            RecordAction(
                booking.BookingId,
                "Created",
                booking.ContactEmail);
            try
            {
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_Bookings_NoDuplicateActiveSlot", StringComparison.Ordinal) == true)
            {
                _db.ChangeTracker.Clear();
                throw new BookingSlotUnavailableException(
                    "This booking was already submitted. Please check your confirmation email.");
            }

            _logger.LogInformation(
                "New booking created for {ProjectNumber} at {StartUtc} by {ContactName} ({ContactEmail}).",
                projectNumber, startUtc, contactName, contactEmail);

            await SendInitialEmailsAsync(booking);
            return booking;
        }

        // --------------------------------------------------
        // Booking cancellation (token-based, public Manage page)
        // --------------------------------------------------
        public async Task<bool> CancelBookingByTokenAsync(Guid token)
        {
            // Guard against double-cancel (prevents double email + churn)
            var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.CancelToken == token);
            if (booking == null)
                return false;

            if (string.Equals(booking.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(booking.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                return false;

            booking.Status = "Cancelled";
            RecordAction(
                booking.BookingId,
                "Cancelled",
                "client-token");
            await _db.SaveChangesAsync();

            _logger.LogInformation("Booking {BookingId} cancelled via token.", booking.BookingId);

            // Emails should never block the cancellation
            await SendCancellationEmailsAsync(booking);
            return true;
        }

        // --------------------------------------------------
        // ASSIGNMENT EMAILS (CLIENT + INSPECTOR)
        // --------------------------------------------------
        public async Task SendAssignmentEmailAsync(Booking booking)
        {
            try
            {
                var tz = _timeRules.TimeZone;
                var startLocal = TimeZoneInfo.ConvertTimeFromUtc(booking.StartUtc, tz);
                var endLocal = TimeZoneInfo.ConvertTimeFromUtc(booking.EndUtc, tz);

                var fromMailbox = _notificationOptions.FromMailbox;
                var manageUrl = BuildManageUrl(booking);

                // -------------------------
                // CLIENT EMAIL
                // -------------------------
                var clientSubject =
                    $"Your Field Review Has Been Scheduled – {startLocal:yyyy-MM-dd HH:mm}";

                var clientBody = BuildAssignmentEmailHtml(
                    booking,
                    startLocal,
                    endLocal,
                    manageUrl,
                    isInspector: false);

                await _graphMail.SendHtmlAsync(
                    fromMailbox,
                    booking.ContactEmail,
                    clientSubject,
                    clientBody);

                // -------------------------
                // INSPECTOR EMAIL
                // -------------------------
                if (!string.IsNullOrWhiteSpace(booking.AssignedTo))
                {
                    var inspector = await GetAssignedInspectorAsync(booking.AssignedTo, requireEnabled: true);

                    if (inspector != null && !string.IsNullOrWhiteSpace(inspector.Email))
                    {
                        var inspectorSubject =
                            $"Field Review Assigned – {booking.ProjectNumber} – {startLocal:yyyy-MM-dd HH:mm}";

                        var inspectorBody = BuildAssignmentEmailHtml(
                            booking,
                            startLocal,
                            endLocal,
                            manageUrl,
                            isInspector: true);

                        await _graphMail.SendHtmlAsync(
                            fromMailbox,
                            inspector.Email,
                            inspectorSubject,
                            inspectorBody);
                    }
                }

                _logger.LogInformation(
                    "Assignment emails sent for booking {BookingId}.", booking.BookingId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to send assignment emails for booking {BookingId}.",
                    booking.BookingId);
            }
        }

        // --------------------------------------------------
        // INITIAL SUBMITTER + ADMIN EMAILS
        // --------------------------------------------------
        private async Task SendInitialEmailsAsync(Booking booking)
        {
            try
            {
                var tz = _timeRules.TimeZone;
                var startLocal = TimeZoneInfo.ConvertTimeFromUtc(booking.StartUtc, tz);
                var endLocal = TimeZoneInfo.ConvertTimeFromUtc(booking.EndUtc, tz);

                var fromMailbox = _notificationOptions.FromMailbox;
                var displayName = _notificationOptions.DisplayName ?? "Kor Structural";

                var manageUrl = BuildManageUrl(booking);

                var submitterSubject =
                    $"Kor Field Review Booking - {booking.ProjectNumber} - {startLocal:yyyy-MM-dd HH:mm}";

                var submitterBody = BuildDetailedBookingHtml(
                    booking,
                    startLocal,
                    endLocal,
                    displayName,
                    manageUrl,
                    BuildProjectInspectionsUrl(booking.ProjectNumber, booking.ContactEmail),
                    isAdmin: false);

                await _graphMail.SendHtmlAsync(
                    fromMailbox,
                    booking.ContactEmail,
                    submitterSubject,
                    submitterBody);

                var adminSubject =
                    $"NEW Field Review Booking – {booking.ProjectNumber} – {startLocal:yyyy-MM-dd HH:mm}";

                var adminBody = BuildDetailedBookingHtml(
                    booking,
                    startLocal,
                    endLocal,
                    displayName,
                    manageUrl,
                    BuildProjectInspectionsUrl(booking.ProjectNumber, _notificationOptions.Email),
                    isAdmin: true);

                await _graphMail.SendHtmlAsync(
                    fromMailbox,
                    _notificationOptions.Email,
                    adminSubject,
                    adminBody);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to send initial booking emails for {BookingId}.",
                    booking.BookingId);
            }
        }

        // --------------------------------------------------
        // CANCELLATION EMAILS (CLIENT + INSPECTOR if assigned)
        // --------------------------------------------------
        public async Task SendCancellationEmailsAsync(Booking booking)
        {
            try
            {
                var tz = _timeRules.TimeZone;
                var startLocal = TimeZoneInfo.ConvertTimeFromUtc(booking.StartUtc, tz);
                var endLocal = TimeZoneInfo.ConvertTimeFromUtc(booking.EndUtc, tz);

                var fromMailbox = _notificationOptions.FromMailbox;
                var displayName = _notificationOptions.DisplayName ?? "Kor Structural";

                // -------------------------
                // CLIENT EMAIL (always)
                // -------------------------
                var clientSubject =
                    $"Field Review Cancelled – {booking.ProjectNumber} – {startLocal:yyyy-MM-dd HH:mm}";

                var clientBody = BuildCancellationEmailHtml(
                    booking, startLocal, endLocal, displayName, isInspector: false);

                await _graphMail.SendHtmlAsync(
                    fromMailbox,
                    booking.ContactEmail,
                    clientSubject,
                    clientBody);

                // -------------------------
                // INSPECTOR EMAIL (only if assigned)
                // -------------------------
                if (!string.IsNullOrWhiteSpace(booking.AssignedTo))
                {
                    // Note: do NOT require Enabled here; if they were assigned, they should be notified.
                    var inspector = await GetAssignedInspectorAsync(booking.AssignedTo, requireEnabled: false);

                    if (inspector != null && !string.IsNullOrWhiteSpace(inspector.Email))
                    {
                        var inspectorSubject =
                            $"Field Review Cancelled – {booking.ProjectNumber} – {startLocal:yyyy-MM-dd HH:mm}";

                        var inspectorBody = BuildCancellationEmailHtml(
                            booking, startLocal, endLocal, displayName, isInspector: true);

                        await _graphMail.SendHtmlAsync(
                            fromMailbox,
                            inspector.Email,
                            inspectorSubject,
                            inspectorBody);
                    }
                }

                _logger.LogInformation(
                    "Cancellation emails sent for booking {BookingId}.", booking.BookingId);
            }
            catch (Exception ex)
            {
                // Never block a cancellation because email failed
                _logger.LogError(ex,
                    "Failed to send cancellation emails for booking {BookingId}.",
                    booking.BookingId);
            }
        }

        private string? BuildManageUrl(Booking booking)
        {
            var baseUrl = (_appOptions.PublicBaseUrl ?? string.Empty).TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl))
                return null;

            return $"{baseUrl}/Manage?token={booking.CancelToken}";
        }

        private string? BuildProjectInspectionsUrl(string? projectNumber, string? recipientEmail)
        {
            var baseUrl = (_appOptions.PublicBaseUrl ?? string.Empty).TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl))
                return null;

            var project = Uri.EscapeDataString(projectNumber ?? string.Empty);
            var email = Uri.EscapeDataString(recipientEmail ?? string.Empty);
            return $"{baseUrl}/Inspections/ByProject?projectNumber={project}&email={email}";
        }

        // --------------------------------------------------
        // EMAIL BUILDERS
        // --------------------------------------------------
        private static string BuildAssignmentEmailHtml(
            Booking booking,
            DateTime startLocal,
            DateTime endLocal,
            string? manageUrl,
            bool isInspector)
        {
            var sb = new StringBuilder();

            if (isInspector)
                sb.Append("<p>You have been assigned a field review.</p>");
            else
                sb.Append("<p>Your field review request has been scheduled.</p>");

            sb.Append("<ul>");
            sb.Append($"<li><strong>Job #:</strong> {WebUtility.HtmlEncode(booking.ProjectNumber)}</li>");
            sb.Append($"<li><strong>Date:</strong> {startLocal:yyyy-MM-dd}</li>");
            sb.Append($"<li><strong>Time:</strong> {BookingDisplayHelper.GetTimeDisplay(
    booking.TimePreference,
    startLocal,
    endLocal)}</li>");

            sb.Append($"<li><strong>Address:</strong> {WebUtility.HtmlEncode(booking.ProjectAddress)}</li>");

            // ADD THIS BLOCK
            if (!string.IsNullOrWhiteSpace(booking.Comments))
            {
                sb.Append($"<li><strong>Details:</strong> {WebUtility.HtmlEncode(booking.Comments)}</li>");
            }

            sb.Append("</ul>");

            if (!string.IsNullOrWhiteSpace(manageUrl) && !isInspector)
            {
                sb.Append("<p>You can view the current status of this booking at:<br/>")
                  .Append("<a href=\"")
                  .Append(WebUtility.HtmlEncode(manageUrl))
                  .Append("\">")
                  .Append(WebUtility.HtmlEncode(manageUrl))
                  .Append("</a></p>");
            }

            sb.Append("<p>Kor Structural</p>");
            return sb.ToString();
        }


        private static string BuildCancellationEmailHtml(
            Booking booking,
            DateTime startLocal,
            DateTime endLocal,
            string displayName,
            bool isInspector)
        {
            var sb = new StringBuilder();

            if (isInspector)
                sb.Append("<p><strong>This field review has been cancelled.</strong></p>");
            else
                sb.Append("<p><strong>Your field review has been cancelled.</strong></p>");

            sb.Append("<ul>");
            sb.Append($"<li><strong>Job #:</strong> {WebUtility.HtmlEncode(booking.ProjectNumber)}</li>");
            sb.Append($"<li><strong>Date:</strong> {startLocal:yyyy-MM-dd}</li>");
            sb.Append($"<li><strong>Time:</strong> {BookingDisplayHelper.GetTimeDisplay(
    booking.TimePreference,
    startLocal,
    endLocal)}</li>");

            sb.Append($"<li><strong>Address:</strong> {WebUtility.HtmlEncode(booking.ProjectAddress)}</li>");

            // ✅ ADD THIS
            if (!string.IsNullOrWhiteSpace(booking.Comments))
            {
                sb.Append($"<li><strong>Details:</strong> {WebUtility.HtmlEncode(booking.Comments)}</li>");
            }

            sb.Append("</ul>");

            if (!isInspector)
            {
                sb.Append("<p>If you still need a field review, please submit a new booking request.</p>");
            }

            sb.Append("<p><strong>Any issues, contact the office - 604-685-9533</strong></p>");

            sb.Append("<p>Regards,<br/>")
              .Append(WebUtility.HtmlEncode(displayName))
              .Append("<br/>Kor Structural</p>");

            return sb.ToString();
        }


        private static string BuildDetailedBookingHtml(
            Booking booking,
            DateTime startLocal,
            DateTime endLocal,
            string displayName,
            string? manageUrl,
            string? projectInspectionsUrl,
            bool isAdmin)
        {
            var sb = new StringBuilder();

            if (isAdmin)
                sb.Append("<p><strong>NEW FIELD REVIEW BOOKING</strong></p>");
            else
                sb.Append("<p>Thank you for contacting Kor. A field review booking has been created with the details below.</p>");

            if (!string.IsNullOrWhiteSpace(manageUrl))
            {
                sb.Append("<p>You can view the current status of this field review and cancel it (if permitted) at:<br/>")
                  .Append("<a href=\"")
                  .Append(WebUtility.HtmlEncode(manageUrl))
                  .Append("\">")
                  .Append(WebUtility.HtmlEncode(manageUrl))
                  .Append("</a></p>");
            }

            if (!string.IsNullOrWhiteSpace(projectInspectionsUrl))
            {
                sb.Append("<p><a href=\"")
                  .Append(WebUtility.HtmlEncode(projectInspectionsUrl))
                  .Append("\">View all inspections for this project</a></p>");
            }

            sb.Append("<table border='1' cellpadding='6' cellspacing='0' ")
              .Append("style='border-collapse:collapse;font-family:Segoe UI,Arial,sans-serif;font-size:13px;'>");

            sb.AppendRow("Kor Job #", booking.ProjectNumber);
            sb.AppendRow("Project Address", booking.ProjectAddress);
            sb.AppendRow("Site Contact",
                $"{booking.ContactName} ({booking.ContactPhone}, {booking.ContactEmail})");
            sb.AppendRow("Requested Date", startLocal.ToString("yyyy-MM-dd"));
            sb.AppendRow(
    "Requested Time",
    BookingDisplayHelper.GetTimeDisplay(
        booking.TimePreference,
        startLocal,
        endLocal));


            if (!string.IsNullOrWhiteSpace(booking.Comments))
                sb.AppendRow("Additional Comments", booking.Comments);

            sb.Append("</table>");

            if (!isAdmin)
            {
                sb.Append("<br/><p><strong>Please note:</strong></p>");
                sb.Append("<ul>");
                sb.Append("<li>Bookings for the following day close at 2:00 p.m. Pacific.</li>");
                sb.Append("<li>Our normal working hours are Monday to Friday, 7:30 a.m. to 4:00 p.m.</li>");
                sb.Append("<li>To change or cancel a booked Field Review, please provide notice by 2:00 p.m. the day before the scheduled review.</li>");
                sb.Append("</ul>");
            }

            sb.Append("<p><strong>Any issues, contact the office - 604-685-9533</strong></p>");

            sb.Append("<p>Regards,<br/>")
              .Append(WebUtility.HtmlEncode(displayName))
              .Append("<br/>Kor Structural</p>");

            return sb.ToString();
        }
    }

    internal static class StringBuilderExtensions
    {
        public static void AppendRow(this StringBuilder sb, string header, string? value)
        {
            sb.Append("<tr><th align='left'>")
              .Append(WebUtility.HtmlEncode(header))
              .Append("</th><td>")
              .Append(WebUtility.HtmlEncode(value ?? string.Empty))
              .Append("</td></tr>");
        }
    }
}
