using System;
using System.Linq;
using System.Threading.Tasks;
using Kor.Inspections.App.Data;
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

public class AdminIndexModelCreateBookingTests
{
    [Fact]
    public async Task OnPostCreateAsync_TomorrowAfterCutoffWithoutOverride_ReturnsPageAndDoesNotCreateBooking()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        await using var db = fixture.CreateContext();
        var model = CreateModel(db, out var nowLocal);

        model.ManualBooking = CreateManualBooking(nowLocal.Date.AddDays(1), overrideCutoff: false);

        var result = await model.OnPostCreateAsync();

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
        Assert.Contains(model.ModelState, entry => entry.Key == "ManualBooking.RequestedDate");

        await using var verifyDb = fixture.CreateContext();
        Assert.Empty(await verifyDb.Bookings.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task OnPostCreateAsync_TomorrowAfterCutoffWithOverride_CreatesBooking()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        await using var db = fixture.CreateContext();
        var model = CreateModel(db, out var nowLocal);

        model.ManualBooking = CreateManualBooking(nowLocal.Date.AddDays(1), overrideCutoff: true);

        var result = await model.OnPostCreateAsync();

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Null(redirect.PageName);
        Assert.Equal("Booking created with cutoff override.", model.StatusMessage);

        await using var verifyDb = fixture.CreateContext();
        var booking = Assert.Single(await verifyDb.Bookings.AsNoTracking().ToListAsync());
        Assert.Equal("30844", booking.ProjectNumber);
        Assert.Equal("client@example.com", booking.ContactEmail);
    }

    [Fact]
    public async Task OnPostCreateAsync_PersistsProjectNumberDisplayWhenSubNumberSubmitted()
    {
        // Regression for V1.1 #3a: admin manual create must store the original
        // sub-numbered input (e.g., "30961-01") on Booking.ProjectNumberDisplay
        // while Booking.ProjectNumber stays Base5 ("30961"). Deltek lookup
        // uses an empty DSN here so it throws; resolvedProjectName falls back
        // to null, exercising the safe-fallback path.
        await using var fixture = await SqlServerFixture.CreateAsync();
        await using var db = fixture.CreateContext();
        var model = CreateModel(db, out var nowLocal);

        model.ManualBooking = CreateManualBooking(nowLocal.Date.AddDays(1), overrideCutoff: true);
        model.ManualBooking.ProjectNumber = "30961-01";

        var result = await model.OnPostCreateAsync();
        Assert.IsType<RedirectToPageResult>(result);

        await using var verifyDb = fixture.CreateContext();
        var booking = Assert.Single(await verifyDb.Bookings.AsNoTracking().ToListAsync());
        Assert.Equal("30961", booking.ProjectNumber);
        Assert.Equal("30961-01", booking.ProjectNumberDisplay);
    }

    [Fact]
    public async Task OnPostCreateAsync_UsesConfiguredDefaultDurationNotHardcoded60()
    {
        // Regression for Codex finding #2: admin create previously hardcoded
        // AddMinutes(60). If InspectionRules.DefaultDurationMinutes is anything
        // else, admin-created bookings would drift from config. This test
        // configures 90 min and verifies the stored endUtc - startUtc matches.
        await using var fixture = await SqlServerFixture.CreateAsync();
        await using var db = fixture.CreateContext();
        var model = CreateModel(db, out var nowLocal, defaultDurationMinutes: 90);

        model.ManualBooking = CreateManualBooking(nowLocal.Date.AddDays(1), overrideCutoff: true);

        var result = await model.OnPostCreateAsync();
        Assert.IsType<RedirectToPageResult>(result);

        await using var verifyDb = fixture.CreateContext();
        var booking = Assert.Single(await verifyDb.Bookings.AsNoTracking().ToListAsync());
        var durationMinutes = (booking.EndUtc - booking.StartUtc).TotalMinutes;
        Assert.Equal(90, durationMinutes);
    }

    private static IndexModel.ManualBookingInput CreateManualBooking(DateTime requestedDateLocal, bool overrideCutoff)
    {
        return new IndexModel.ManualBookingInput
        {
            ProjectNumber = "30844-01",
            ProjectAddress = "123 Test St",
            ContactName = "Jane Doe",
            ContactPhone = "6045551212",
            ContactEmail = "client@example.com",
            Comments = "Beam inspection at gridline B.",
            RequestedDate = requestedDateLocal,
            RequestedTime = "08:00",
            OverrideCutoff = overrideCutoff
        };
    }

    private static IndexModel CreateModel(InspectionsContext db, out DateTime nowLocal, int defaultDurationMinutes = 60)
    {
        var timeZone = TimeRuleServiceTestFactory.FindZone(localNow =>
            localNow.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday &&
            localNow.Hour >= 15 &&
            localNow.Hour <= 22);
        nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);
        var timeRules = TimeRuleServiceTestFactory.Create(timeZone, cutoffHourLocal: 14, defaultDurationMinutes: defaultDurationMinutes);

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
                CutoffHourLocal = 14,
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
            var fixture = new SqlServerFixture("KorAdminIndexCreateTests_" + Guid.NewGuid().ToString("N"));
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
