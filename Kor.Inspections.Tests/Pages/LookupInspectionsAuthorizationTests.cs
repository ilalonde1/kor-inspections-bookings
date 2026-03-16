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

public class LookupInspectionsAuthorizationTests
{
    [Fact]
    public async Task OnPostLookupInspectionsAsync_MissingEmail_Returns400()
    {
        await using var db = CreateContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var model = CreateModel(db, cache);

        var result = await model.OnPostLookupInspectionsAsync(new IndexModel.LookupContactsRequest
        {
            ProjectNumber = "30844",
            Email = ""
        });

        Assert.Equal(StatusCodes.Status400BadRequest, model.Response.StatusCode);
        Assert.Contains("required", JsonSerializer.Serialize(result.Value), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OnPostLookupInspectionsAsync_UnverifiedEmail_Returns403()
    {
        await using var db = CreateContext();
        await SeedBookingAsync(db, "30844", "verified@example.com");
        var cache = new MemoryCache(new MemoryCacheOptions());
        var model = CreateModel(db, cache);

        var result = await model.OnPostLookupInspectionsAsync(new IndexModel.LookupContactsRequest
        {
            ProjectNumber = "30844",
            Email = "verified@example.com"
        });

        Assert.Equal(StatusCodes.Status403Forbidden, model.Response.StatusCode);
        Assert.Contains("verify", JsonSerializer.Serialize(result.Value), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OnPostLookupInspectionsAsync_VerifiedMatchingEmail_ReturnsOnlyThatUsersBookings()
    {
        await using var db = CreateContext();
        await SeedBookingAsync(db, "30844", "verified@example.com");
        await SeedBookingAsync(db, "30844", "other@example.com");

        db.ProjectDefaults.Add(new ProjectDefault
        {
            ProjectNumber = "30844",
            EmailDomain = "example.com",
            UpdatedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var cache = new MemoryCache(new MemoryCacheOptions());
        var model = CreateModel(db, cache);

        var result = await model.OnPostLookupInspectionsAsync(new IndexModel.LookupContactsRequest
        {
            ProjectNumber = "30844",
            Email = "verified@example.com"
        });

        var payload = Assert.IsType<JsonResult>(result);
        var inspections = Assert.IsAssignableFrom<IEnumerable<IndexModel.InspectionDto>>(payload.Value).ToList();

        Assert.Equal(StatusCodes.Status200OK, model.Response.StatusCode == 0 ? StatusCodes.Status200OK : model.Response.StatusCode);
        Assert.Single(inspections);
        Assert.Equal("verified@example.com", inspections[0].ContactEmail);
    }

    /// <summary>
    /// Regression test for the +7-hour display bug.
    /// SQL Server returns DateTime with Kind=Unspecified; System.Text.Json omits the 'Z' suffix
    /// for Unspecified datetimes, causing JS new Date() to treat a UTC value as local time.
    /// The handler must normalise Kind to Utc before serialising.
    /// </summary>
    [Fact]
    public async Task OnPostLookupInspectionsAsync_StartUtcKindIsUtc_SoJsonIncludesZSuffix()
    {
        await using var db = CreateContext();

        // Simulate what EF Core returns from SQL Server datetime columns: Kind = Unspecified
        var unspecifiedStart = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(2), DateTimeKind.Unspecified);
        var unspecifiedEnd   = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(2).AddHours(1), DateTimeKind.Unspecified);

        db.Bookings.Add(new Booking
        {
            BookingId      = Guid.NewGuid(),
            CancelToken    = Guid.NewGuid(),
            ProjectNumber  = "30844",
            ProjectAddress = "123 Test St",
            ContactName    = "Jane Doe",
            ContactPhone   = "6045551212",
            ContactEmail   = "tz@example.com",
            StartUtc       = unspecifiedStart,
            EndUtc         = unspecifiedEnd,
            Status         = "Unassigned",
            CreatedUtc     = DateTime.UtcNow
        });
        db.ProjectDefaults.Add(new ProjectDefault
        {
            ProjectNumber = "30844",
            EmailDomain   = "example.com",
            UpdatedUtc    = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var cache = new MemoryCache(new MemoryCacheOptions());
        var model = CreateModel(db, cache);

        var result = await model.OnPostLookupInspectionsAsync(new IndexModel.LookupContactsRequest
        {
            ProjectNumber = "30844",
            Email         = "tz@example.com"
        });

        var payload     = Assert.IsType<JsonResult>(result);
        var inspections = Assert.IsAssignableFrom<IEnumerable<IndexModel.InspectionDto>>(payload.Value).ToList();
        Assert.Single(inspections);

        // Kind must be Utc so System.Text.Json emits the 'Z' suffix and the
        // browser's new Date() interprets it as UTC rather than local time.
        Assert.Equal(DateTimeKind.Utc, inspections[0].StartUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, inspections[0].EndUtc.Kind);
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
            HttpContext = new DefaultHttpContext()
        };

        return model;
    }

    private static async Task SeedBookingAsync(InspectionsContext db, string projectNumber, string contactEmail)
    {
        db.Bookings.Add(new Booking
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
            Status = "Unassigned",
            CreatedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static InspectionsContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<InspectionsContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new InspectionsContext(options);
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
