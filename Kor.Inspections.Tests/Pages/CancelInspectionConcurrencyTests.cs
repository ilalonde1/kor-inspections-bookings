using System.Security.Claims;
using System.Text.Json;
using Kor.Inspections.App.Data;
using Kor.Inspections.App.Data.Models;
using Kor.Inspections.App.Options;
using Kor.Inspections.App.Pages;
using Kor.Inspections.App.Services;
using Kor.Inspections.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Kor.Inspections.Tests.Pages;

public class CancelInspectionConcurrencyTests
{
    [Fact]
    public async Task OnPostCancelInspectionAsync_ConcurrentModification_WonByCancel_ReturnsOk()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        var booking = await fixture.SeedBookingAsync("Unassigned", "30844", "jane@acme.com");
        await fixture.SeedProjectDefaultAsync("30844", "acme.com");

        var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set("proj-bootstrap:30844|acme.com", true);

        await using var staleContext = fixture.CreateContext();
        _ = await staleContext.Bookings.SingleAsync(b => b.BookingId == booking.BookingId);

        await using (var freshContext = fixture.CreateContext())
        {
            var current = await freshContext.Bookings.SingleAsync(b => b.BookingId == booking.BookingId);
            current.Status = "Cancelled";
            await freshContext.SaveChangesAsync();
        }

        var model = CreateModel(staleContext, cache);
        var result = await model.OnPostCancelInspectionAsync(new IndexModel.CancelInspectionRequest
        {
            Id = booking.BookingId.ToString(),
            ProjectNumber = "30844",
            Email = "jane@acme.com"
        });

        Assert.Equal(StatusCodes.Status200OK, model.Response.StatusCode == 0 ? StatusCodes.Status200OK : model.Response.StatusCode);
        Assert.Equal("{\"ok\":true}", JsonSerializer.Serialize(result.Value));
    }

    [Fact]
    public async Task OnPostCancelInspectionAsync_ConcurrentModification_WonByOtherChange_Returns409()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        var booking = await fixture.SeedBookingAsync("Unassigned", "30844", "jane@acme.com");
        await fixture.SeedProjectDefaultAsync("30844", "acme.com");

        var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set("proj-bootstrap:30844|acme.com", true);

        await using var staleContext = fixture.CreateContext();
        _ = await staleContext.Bookings.SingleAsync(b => b.BookingId == booking.BookingId);

        await using (var freshContext = fixture.CreateContext())
        {
            var current = await freshContext.Bookings.SingleAsync(b => b.BookingId == booking.BookingId);
            current.Status = "Assigned";
            await freshContext.SaveChangesAsync();
        }

        var model = CreateModel(staleContext, cache);
        var result = await model.OnPostCancelInspectionAsync(new IndexModel.CancelInspectionRequest
        {
            Id = booking.BookingId.ToString(),
            ProjectNumber = "30844",
            Email = "jane@acme.com"
        });

        Assert.Equal(StatusCodes.Status409Conflict, model.Response.StatusCode);
        Assert.Contains("concurrently", JsonSerializer.Serialize(result.Value), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnPostCancelInspectionAsync_HappyPath_CancelsAndWritesAuditRecord()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        var booking = await fixture.SeedBookingAsync("Unassigned", "30844", "jane@acme.com");
        await fixture.SeedProjectDefaultAsync("30844", "acme.com");

        var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set("proj-bootstrap:30844|acme.com", true);

        await using var db = fixture.CreateContext();
        var model = CreateModel(db, cache);

        var result = await model.OnPostCancelInspectionAsync(new IndexModel.CancelInspectionRequest
        {
            Id = booking.BookingId.ToString(),
            ProjectNumber = "30844",
            Email = "jane@acme.com"
        });

        Assert.Equal(StatusCodes.Status200OK, model.Response.StatusCode == 0 ? StatusCodes.Status200OK : model.Response.StatusCode);
        Assert.Equal("{\"ok\":true}", JsonSerializer.Serialize(result.Value));

        await using var verifyDb = fixture.CreateContext();
        var updated = await verifyDb.Bookings.AsNoTracking().SingleAsync(b => b.BookingId == booking.BookingId);
        var action = Assert.Single(await verifyDb.BookingActions.AsNoTracking().ToListAsync());
        Assert.Equal("Cancelled", updated.Status);
        Assert.Equal("Cancelled", action.ActionType);
        Assert.Equal("jane@acme.com", action.PerformedBy);
    }

    private static IndexModel CreateModel(InspectionsContext db, IMemoryCache cache)
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
            new ProjectProfileService(db),
            new DeltekProjectService(
                Options.Create(new DeltekProjectOptions()),
                cache,
                NullLogger<DeltekProjectService>.Instance),
            new ProjectBootstrapVerificationService(
                cache,
                new GraphMailService(new ThrowingTokenProvider(), new NoOpHttpClientFactory()),
                Options.Create(new NotificationOptions
                {
                    FromMailbox = "reviews@example.com",
                    Email = "reviews@example.com",
                    DisplayName = "KOR Reviews"
                }),
                db,
                NullLogger<ProjectBootstrapVerificationService>.Instance),
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
            NullLogger<IndexModel>.Instance);

        model.PageContext = new PageContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, "jane@acme.com")],
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
            var fixture = new SqlServerFixture("KorCancelInspectionTests_" + Guid.NewGuid().ToString("N"));
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

        public async Task<Booking> SeedBookingAsync(string status, string projectNumber, string contactEmail)
        {
            var booking = new Booking
            {
                BookingId = Guid.NewGuid(),
                CancelToken = Guid.NewGuid(),
                ProjectNumber = projectNumber,
                ProjectAddress = "123 Test St",
                ContactName = "Jane Doe",
                ContactPhone = "6045551212",
                ContactEmail = contactEmail,
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

        public async Task SeedProjectDefaultAsync(string projectNumber, string domain)
        {
            await using var db = CreateContext();
            db.ProjectDefaults.Add(new ProjectDefault
            {
                ProjectNumber = projectNumber,
                EmailDomain = domain,
                UpdatedUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
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
