using Kor.Inspections.App.Data;
using Kor.Inspections.App.Data.Models;
using Kor.Inspections.App.Options;
using Kor.Inspections.App.Pages.Admin;
using Kor.Inspections.App.Services;
using Kor.Inspections.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace Kor.Inspections.Tests.Pages;

public class AdminIndexModelReassignmentTests
{
    [Fact]
    public async Task OnPostAssignAsync_ReassignFromInspectorAToInspectorB_SendsAssignmentEmail()
    {
        // Regression for Codex finding #3: OnPostAssignAsync only sent mail
        // on Unassigned -> Assigned transitions; reassignments A -> B were
        // silent so neither the client nor the new inspector was notified.
        await using var fixture = await SqlServerFixture.CreateAsync("KorAdminReassignTests_");

        var inspectorA = await SeedInspectorAsync(fixture, "Inspector A", "a@example.com");
        var inspectorB = await SeedInspectorAsync(fixture, "Inspector B", "b@example.com");
        var booking = await SeedAssignedBookingAsync(fixture, inspectorA.Email);

        var emailHandler = new CountingHttpMessageHandler();
        await using var db = fixture.CreateContext();
        var model = CreateModel(db, emailHandler);

        var result = await model.OnPostAssignAsync(booking.BookingId, inspectorB.Email);

        Assert.IsType<RedirectToPageResult>(result);

        await using var verify = fixture.CreateContext();
        var updated = await verify.Bookings.AsNoTracking().SingleAsync(b => b.BookingId == booking.BookingId);
        Assert.Equal(inspectorB.Email, updated.AssignedTo);

        // Two recipients go through the send pipeline: client + new inspector.
        Assert.Equal(2, emailHandler.RequestCount);
        Assert.Contains(
            emailHandler.CapturedBodies,
            body => body.Contains("Inspector Has Changed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OnPostAssignAsync_ReassignToSameInspector_DoesNotSendEmail()
    {
        // Idempotent guard: if admin clicks Assign with the already-assigned
        // inspector still selected, no change and no duplicate notification.
        await using var fixture = await SqlServerFixture.CreateAsync("KorAdminReassignTests_");

        var inspectorA = await SeedInspectorAsync(fixture, "Inspector A", "a@example.com");
        var booking = await SeedAssignedBookingAsync(fixture, inspectorA.Email);

        var emailHandler = new CountingHttpMessageHandler();
        await using var db = fixture.CreateContext();
        var model = CreateModel(db, emailHandler);

        var result = await model.OnPostAssignAsync(booking.BookingId, inspectorA.Email);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(0, emailHandler.RequestCount);
    }

    [Fact]
    public async Task OnPostAssignAsync_ChangeToUnassigned_DoesNotSendEmail()
    {
        // Explicit scope note: unassign (X -> null) does not send a new email
        // here. A dedicated "inspector removed" notification, if desired, is
        // separate scope.
        await using var fixture = await SqlServerFixture.CreateAsync("KorAdminReassignTests_");

        var inspectorA = await SeedInspectorAsync(fixture, "Inspector A", "a@example.com");
        var booking = await SeedAssignedBookingAsync(fixture, inspectorA.Email);

        var emailHandler = new CountingHttpMessageHandler();
        await using var db = fixture.CreateContext();
        var model = CreateModel(db, emailHandler);

        var result = await model.OnPostAssignAsync(booking.BookingId, "Unassigned");

        Assert.IsType<RedirectToPageResult>(result);

        await using var verify = fixture.CreateContext();
        var updated = await verify.Bookings.AsNoTracking().SingleAsync(b => b.BookingId == booking.BookingId);
        Assert.Null(updated.AssignedTo);
        Assert.Equal("Unassigned", updated.Status);
        Assert.Equal(0, emailHandler.RequestCount);
    }

    [Fact]
    public async Task OnPostAssignAsync_InitialAssignmentToInspector_SendsScheduledSubject()
    {
        await using var fixture = await SqlServerFixture.CreateAsync("KorAdminReassignTests_");
        var inspectorA = await SeedInspectorAsync(fixture, "Inspector A", "a@example.com");
        var booking = await SeedAssignedBookingAsync(fixture, assignedToEmail: null);

        var emailHandler = new CountingHttpMessageHandler();
        await using var db = fixture.CreateContext();
        var model = CreateModel(db, emailHandler);

        var result = await model.OnPostAssignAsync(booking.BookingId, inspectorA.Email);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(2, emailHandler.RequestCount);
        Assert.Contains(
            emailHandler.CapturedBodies,
            body => body.Contains("Has Been Scheduled", StringComparison.Ordinal) &&
                    !body.Contains("Inspector Has Changed", StringComparison.Ordinal));
    }

    // --------------------------------------------------
    // HELPERS
    // --------------------------------------------------

    private static IndexModel CreateModel(InspectionsContext db, CountingHttpMessageHandler emailHandler)
    {
        var timeZone = TimeRuleServiceTestFactory.FindZone(nowLocal =>
            nowLocal.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday &&
            nowLocal.Hour <= 22);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);
        var timeRules = TimeRuleServiceTestFactory.Create(timeZone, nowLocal.Hour + 1);

        var bookingService = new BookingService(
            db,
            Options.Create(new NotificationOptions
            {
                FromMailbox = "reviews@example.com",
                AdminRecipientEmail = "reviews@example.com",
                DisplayName = "KOR Reviews"
            }),
            NullLogger<BookingService>.Instance,
            timeRules,
            new GraphMailService(new FixedTokenProvider(), new CountingHttpClientFactory(emailHandler)),
            Options.Create(new InspectionRulesOptions
            {
                CutoffHourLocal = nowLocal.Hour + 1,
                BookingWindowDays = 7,
                SlotMinutes = 30,
                DefaultDurationMinutes = 60,
                TravelPaddingMinutes = 15,
                MaxBookingsPerSlot = 3,
                WorkStart = "07:30",
                WorkEnd = "16:00",
                TimeZoneId = timeZone.Id
            }),
            Options.Create(new AppOptions { PublicBaseUrl = "https://example.com" }));

        var deltekProjectService = new DeltekProjectService(
            Options.Create(new DeltekProjectOptions()),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<DeltekProjectService>.Instance);

        var model = new IndexModel(
            db,
            timeRules,
            bookingService,
            deltekProjectService,
            NullLogger<IndexModel>.Instance);

        model.PageContext = new PageContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        [new Claim(ClaimTypes.Name, "admin@example.com")],
                        "TestAuth"))
            },
            ViewData = new ViewDataDictionary(
                new EmptyModelMetadataProvider(),
                new ModelStateDictionary())
        };

        return model;
    }

    private static async Task<Inspector> SeedInspectorAsync(SqlServerFixture fixture, string displayName, string email)
    {
        var inspector = new Inspector
        {
            DisplayName = displayName,
            Email = email,
            Enabled = true
        };

        await using var db = fixture.CreateContext();
        db.Inspectors.Add(inspector);
        await db.SaveChangesAsync();
        return inspector;
    }

    private static async Task<Booking> SeedAssignedBookingAsync(SqlServerFixture fixture, string? assignedToEmail)
    {
        var booking = new Booking
        {
            BookingId = Guid.NewGuid(),
            CancelToken = Guid.NewGuid(),
            ProjectNumber = "30844",
            ProjectAddress = "123 Test St",
            ContactName = "Jane Doe",
            ContactPhone = "6045551212",
            ContactEmail = "jane@example.com",
            StartUtc = DateTime.UtcNow.AddDays(2),
            EndUtc = DateTime.UtcNow.AddDays(2).AddHours(1),
            Status = assignedToEmail is null ? "Unassigned" : "Assigned",
            AssignedTo = assignedToEmail,
            CreatedUtc = DateTime.UtcNow
        };

        await using var db = fixture.CreateContext();
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return booking;
    }

    private sealed class FixedTokenProvider : IGraphTokenProvider
    {
        public Task<string> GetTokenAsync() => Task.FromResult("test-token");
    }

    private sealed class CountingHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public CountingHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class CountingHttpMessageHandler : HttpMessageHandler
    {
        private int _requestCount;

        public int RequestCount => _requestCount;
        public List<string> CapturedBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            CapturedBodies.Add(body);
            return new HttpResponseMessage(System.Net.HttpStatusCode.Accepted);
        }
    }
}
