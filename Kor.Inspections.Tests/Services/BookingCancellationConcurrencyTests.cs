using System.Collections.Concurrent;
using Kor.Inspections.App.Data;
using Kor.Inspections.App.Data.Models;
using Kor.Inspections.App.Options;
using Kor.Inspections.App.Services;
using Kor.Inspections.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Kor.Inspections.Tests.Services;

public class BookingCancellationConcurrencyTests
{
    [Fact]
    public async Task CancelBookingByTokenAsync_ConcurrentCalls_WriteOneAuditRecordAndSendOneEmail()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        var booking = await fixture.SeedBookingAsync("Unassigned");
        var gate = new SaveGate(2);
        var emailHandler = new CountingHttpMessageHandler();

        await using var db1 = fixture.CreateCoordinatedContext(gate);
        await using var db2 = fixture.CreateCoordinatedContext(gate);
        var service1 = CreateBookingService(db1, emailHandler);
        var service2 = CreateBookingService(db2, emailHandler);

        var results = await Task.WhenAll(
            service1.CancelBookingByTokenAsync(booking.CancelToken),
            service2.CancelBookingByTokenAsync(booking.CancelToken));

        await using var verifyDb = fixture.CreateContext();
        var updated = await verifyDb.Bookings.AsNoTracking().SingleAsync(b => b.BookingId == booking.BookingId);
        var actions = await verifyDb.BookingActions.AsNoTracking().ToListAsync();

        Assert.All(results, Assert.True);
        Assert.Equal("Cancelled", updated.Status);
        var action = Assert.Single(actions);
        Assert.Equal("Cancelled", action.ActionType);
        Assert.Equal("client-token", action.PerformedBy);
        Assert.Equal(1, emailHandler.RequestCount);
    }

    [Fact]
    public async Task CancelBookingByTokenAsync_UnassignedBooking_ReturnsTrueAndCancels()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        var booking = await fixture.SeedBookingAsync("Unassigned");
        var emailHandler = new CountingHttpMessageHandler();

        await using var db = fixture.CreateContext();
        var service = CreateBookingService(db, emailHandler);

        var result = await service.CancelBookingByTokenAsync(booking.CancelToken);

        await using var verifyDb = fixture.CreateContext();
        var updated = await verifyDb.Bookings.AsNoTracking().SingleAsync(b => b.BookingId == booking.BookingId);
        Assert.True(result);
        Assert.Equal("Cancelled", updated.Status);
    }

    [Fact]
    public async Task CancelBookingByTokenAsync_AlreadyCancelled_ReturnsTrueAndDoesNotWriteAction()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        var booking = await fixture.SeedBookingAsync("Cancelled");
        var emailHandler = new CountingHttpMessageHandler();

        await using var db = fixture.CreateContext();
        var service = CreateBookingService(db, emailHandler);

        var result = await service.CancelBookingByTokenAsync(booking.CancelToken);

        await using var verifyDb = fixture.CreateContext();
        Assert.True(result);
        Assert.Empty(await verifyDb.BookingActions.AsNoTracking().ToListAsync());
        Assert.Equal(0, emailHandler.RequestCount);
    }

    private static BookingService CreateBookingService(InspectionsContext db, CountingHttpMessageHandler emailHandler)
    {
        var timeZone = TimeRuleServiceTestFactory.FindZone(nowLocal =>
            nowLocal.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday &&
            nowLocal.Hour <= 22);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);
        var timeRules = TimeRuleServiceTestFactory.Create(timeZone, nowLocal.Hour + 1);

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
            Options.Create(new AppOptions()));
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
            var fixture = new SqlServerFixture("KorCancelTests_" + Guid.NewGuid().ToString("N"));
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

        public CoordinatedInspectionsContext CreateCoordinatedContext(SaveGate gate)
        {
            var options = new DbContextOptionsBuilder<InspectionsContext>()
                .UseSqlServer(_connectionString)
                .Options;

            return new CoordinatedInspectionsContext(options, gate);
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

        public async ValueTask DisposeAsync()
        {
            await using var db = CreateContext();
            await db.Database.EnsureDeletedAsync();
        }
    }

    private sealed class CoordinatedInspectionsContext : InspectionsContext
    {
        private readonly SaveGate _gate;

        public CoordinatedInspectionsContext(DbContextOptions<InspectionsContext> options, SaveGate gate)
            : base(options)
        {
            _gate = gate;
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (ChangeTracker.Entries<Booking>().Any(e => e.State == EntityState.Modified))
            {
                await _gate.SignalAndWaitAsync(cancellationToken);
            }

            return await base.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class SaveGate
    {
        private readonly int _participants;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public SaveGate(int participants)
        {
            _participants = participants;
        }

        public Task SignalAndWaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _arrivals) >= _participants)
            {
                _ready.TrySetResult();
            }

            return _ready.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class FixedTokenProvider : IGraphTokenProvider
    {
        public Task<string> GetTokenAsync() => Task.FromResult("token");
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
