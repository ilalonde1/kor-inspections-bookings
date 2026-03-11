using Kor.Inspections.App.Data;
using Kor.Inspections.App.Data.Models;
using Kor.Inspections.App.Options;
using Kor.Inspections.App.Pages.Admin;
using Kor.Inspections.App.Services;
using Kor.Inspections.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace Kor.Inspections.Tests.Pages;

public class AdminIndexModelConcurrencyTests
{
    [Fact]
    public async Task OnPostAssignAsync_ConcurrentModification_SetsConcurrencyStatusMessage()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        var booking = await fixture.SeedBookingAsync("Unassigned");
        var inspector = await fixture.SeedInspectorAsync("Inspector One", "inspector@example.com");

        await using var staleContext = fixture.CreateContext();
        _ = await staleContext.Bookings.SingleAsync(b => b.BookingId == booking.BookingId);

        await using (var freshContext = fixture.CreateContext())
        {
            var updated = await freshContext.Bookings.SingleAsync(b => b.BookingId == booking.BookingId);
            updated.AssignedTo = "other@example.com";
            updated.Status = "Assigned";
            await freshContext.SaveChangesAsync();
        }

        var model = CreateModel(staleContext);

        var result = await model.OnPostAssignAsync(booking.BookingId, inspector.Email);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Null(redirect.PageName);
        Assert.Contains("modified by another user", model.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnPostCancelAsync_ConcurrentModification_SetsConcurrencyStatusMessage()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        var booking = await fixture.SeedBookingAsync("Unassigned");

        await using var staleContext = fixture.CreateContext();
        _ = await staleContext.Bookings.SingleAsync(b => b.BookingId == booking.BookingId);

        await using (var freshContext = fixture.CreateContext())
        {
            var updated = await freshContext.Bookings.SingleAsync(b => b.BookingId == booking.BookingId);
            updated.AssignedTo = "other@example.com";
            await freshContext.SaveChangesAsync();
        }

        var model = CreateModel(staleContext);

        var result = await model.OnPostCancelAsync(booking.BookingId);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Null(redirect.PageName);
        Assert.Contains("modified by another user", model.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnPostCancelAsync_UnmodifiedBooking_CancelsSuccessfully()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        var booking = await fixture.SeedBookingAsync("Unassigned");

        await using var db = fixture.CreateContext();
        var model = CreateModel(db);

        var result = await model.OnPostCancelAsync(booking.BookingId);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Null(redirect.PageName);

        await using var verifyDb = fixture.CreateContext();
        var updated = await verifyDb.Bookings.AsNoTracking().SingleAsync(b => b.BookingId == booking.BookingId);
        var action = Assert.Single(await verifyDb.BookingActions.AsNoTracking().ToListAsync());
        Assert.Equal("Cancelled", updated.Status);
        Assert.Equal("Cancelled", action.ActionType);
    }

    private static IndexModel CreateModel(InspectionsContext db)
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
            new GraphMailService(new ThrowingTokenProvider(), new NoOpHttpClientFactory()),
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
            Options.Create(new AppOptions()));

        var model = new IndexModel(
            db,
            timeRules,
            bookingService,
            NullLogger<IndexModel>.Instance);

        model.PageContext = new PageContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        [new Claim(ClaimTypes.Name, "admin@example.com")],
                        "TestAuth"))
            }
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
            var fixture = new SqlServerFixture("KorAdminIndexTests_" + Guid.NewGuid().ToString("N"));
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

        public async Task<Booking> SeedBookingAsync(string status)
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
                Status = status,
                CreatedUtc = DateTime.UtcNow
            };

            await using var db = CreateContext();
            db.Bookings.Add(booking);
            await db.SaveChangesAsync();
            return booking;
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

        public async ValueTask DisposeAsync()
        {
            await using var db = CreateContext();
            await db.Database.EnsureDeletedAsync();
        }
    }

    private sealed class ThrowingTokenProvider : IGraphTokenProvider
    {
        public Task<string> GetTokenAsync()
        {
            throw new InvalidOperationException("Missing Graph configuration.");
        }
    }

    private sealed class NoOpHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
