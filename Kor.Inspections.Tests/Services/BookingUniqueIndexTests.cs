using Kor.Inspections.App.Data;
using Kor.Inspections.App.Data.Models;
using Kor.Inspections.App.Options;
using Kor.Inspections.App.Services;
using Kor.Inspections.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Kor.Inspections.Tests.Services;

public class BookingUniqueIndexTests
{
    [Fact]
    public async Task CreateBookingAsync_DuplicateActiveBooking_ThrowsAndLeavesSingleRow()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        var startUtc = DateTime.UtcNow.AddDays(3);
        var endUtc = startUtc.AddHours(1);

        await using (var seedContext = fixture.CreateContext())
        {
            seedContext.Bookings.Add(CreateBooking("30844", "alice@example.com", startUtc, endUtc, "Unassigned"));
            await seedContext.SaveChangesAsync();
        }

        await using var db = fixture.CreateContext();
        var service = CreateBookingService(db);

        var ex = await Assert.ThrowsAsync<BookingSlotUnavailableException>(() =>
            service.CreateBookingAsync(
                "30844",
                "123 Test St",
                "Jane Doe",
                "6045551212",
                "alice@example.com",
                "Duplicate submission",
                startUtc,
                endUtc,
                null));

        Assert.Equal("This booking was already submitted. Please check your confirmation email.", ex.Message);
        Assert.Equal(1, await db.Bookings.CountAsync());
    }

    [Fact]
    public async Task SaveChangesAsync_CancelledBooking_DoesNotBlockNewActiveBooking()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        var startUtc = DateTime.UtcNow.AddDays(3);
        var endUtc = startUtc.AddHours(1);

        await using var db = fixture.CreateContext();
        db.Bookings.Add(CreateBooking("30844", "alice@example.com", startUtc, endUtc, "Cancelled"));
        db.Bookings.Add(CreateBooking("30844", "alice@example.com", startUtc, endUtc, "Unassigned"));

        await db.SaveChangesAsync();

        Assert.Equal(2, await db.Bookings.CountAsync());
    }

    [Fact]
    public async Task SaveChangesAsync_DifferentContactEmail_AllowsBothBookings()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        var startUtc = DateTime.UtcNow.AddDays(3);
        var endUtc = startUtc.AddHours(1);

        await using var db = fixture.CreateContext();
        db.Bookings.Add(CreateBooking("30844", "alice@example.com", startUtc, endUtc, "Unassigned"));
        db.Bookings.Add(CreateBooking("30844", "bob@example.com", startUtc, endUtc, "Unassigned"));

        await db.SaveChangesAsync();

        Assert.Equal(2, await db.Bookings.CountAsync());
    }

    private static BookingService CreateBookingService(InspectionsContext db)
    {
        var timeZone = TimeRuleServiceTestFactory.FindZone(nowLocal =>
            nowLocal.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday &&
            nowLocal.Hour <= 22);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);
        var timeRules = TimeRuleServiceTestFactory.Create(timeZone, nowLocal.Hour + 1, maxBookingsPerSlot: 10);

        var graphMail = new GraphMailService(new ThrowingTokenProvider(), new NoOpHttpClientFactory());
        return new BookingService(
            db,
            Options.Create(new NotificationOptions
            {
                FromMailbox = "reviews@example.com",
                Email = "reviews@example.com",
                DisplayName = "KOR Reviews"
            }),
            NullLogger<BookingService>.Instance,
            timeRules,
            graphMail,
            Options.Create(new InspectionRulesOptions
            {
                CutoffHourLocal = nowLocal.Hour + 1,
                BookingWindowDays = 7,
                SlotMinutes = 30,
                DefaultDurationMinutes = 60,
                TravelPaddingMinutes = 15,
                MaxBookingsPerSlot = 10,
                WorkStart = "07:30",
                WorkEnd = "16:00",
                TimeZoneId = timeZone.Id
            }),
            Options.Create(new AppOptions()));
    }

    private static Booking CreateBooking(string projectNumber, string contactEmail, DateTime startUtc, DateTime endUtc, string status)
    {
        return new Booking
        {
            BookingId = Guid.NewGuid(),
            CancelToken = Guid.NewGuid(),
            ProjectNumber = projectNumber,
            ProjectAddress = "123 Test St",
            ContactName = "Jane Doe",
            ContactPhone = "6045551212",
            ContactEmail = contactEmail,
            StartUtc = startUtc,
            EndUtc = endUtc,
            Status = status,
            CreatedUtc = DateTime.UtcNow
        };
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
            var fixture = new SqlServerFixture("KorInspectTests_" + Guid.NewGuid().ToString("N"));
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
