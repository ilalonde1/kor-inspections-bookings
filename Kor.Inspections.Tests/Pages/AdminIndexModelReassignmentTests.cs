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
        await using var fixture = await SqlServerFixture.CreateAsync();

        var inspectorA = await fixture.SeedInspectorAsync("Inspector A", "a@example.com");
        var inspectorB = await fixture.SeedInspectorAsync("Inspector B", "b@example.com");
        var booking = await fixture.SeedAssignedBookingAsync(inspectorA.Email);

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
    }

    [Fact]
    public async Task OnPostAssignAsync_ReassignToSameInspector_DoesNotSendEmail()
    {
        // Idempotent guard: if admin clicks Assign with the already-assigned
        // inspector still selected, no change and no duplicate notification.
        await using var fixture = await SqlServerFixture.CreateAsync();

        var inspectorA = await fixture.SeedInspectorAsync("Inspector A", "a@example.com");
        var booking = await fixture.SeedAssignedBookingAsync(inspectorA.Email);

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
        await using var fixture = await SqlServerFixture.CreateAsync();

        var inspectorA = await fixture.SeedInspectorAsync("Inspector A", "a@example.com");
        var booking = await fixture.SeedAssignedBookingAsync(inspectorA.Email);

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
                Email = "reviews@example.com",
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

    private sealed class SqlServerFixture : IAsyncDisposable
    {
        private readonly string _connectionString;

        private SqlServerFixture(string databaseName)
        {
            _connectionString = $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True;";
        }

        public static async Task<SqlServerFixture> CreateAsync()
        {
            var fixture = new SqlServerFixture("KorAdminReassignTests_" + Guid.NewGuid().ToString("N"));
            await using var db = fixture.CreateContext();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
            return fixture;
        }

        public InspectionsContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<InspectionsContext>()
                .UseSqlServer(_connectionString)
                .Options;

            return new InspectionsContext(options);
        }

        public async Task<Inspector> SeedInspectorAsync(string displayName, string email)
        {
            var inspector = new Inspector
            {
                DisplayName = displayName,
                Email = email,
                Enabled = true
            };

            await using var db = CreateContext();
            db.Inspectors.Add(inspector);
            await db.SaveChangesAsync();
            return inspector;
        }

        public async Task<Booking> SeedAssignedBookingAsync(string assignedToEmail)
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
                Status = "Assigned",
                AssignedTo = assignedToEmail,
                CreatedUtc = DateTime.UtcNow
            };

            await using var db = CreateContext();
            db.Bookings.Add(booking);
            await db.SaveChangesAsync();
            return booking;
        }

        public async ValueTask DisposeAsync()
        {
            await using var db = CreateContext();
            await db.Database.EnsureDeletedAsync();
        }
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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Accepted));
        }
    }
}
