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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace Kor.Inspections.Tests.Pages;

public class AdminIndexModelLoggingTests
{
    [Fact]
    public async Task OnPostAssignAsync_SuccessfulAssignment_LogsInformationWithBookingId()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        var booking = await fixture.SeedBookingAsync("Unassigned");
        var inspector = await fixture.SeedInspectorAsync("Inspector One", "inspector@example.com");

        await using var db = fixture.CreateContext();
        var logger = new ListLogger<IndexModel>();
        var model = CreateModel(db, logger);

        var result = await model.OnPostAssignAsync(booking.BookingId, inspector.Email);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Null(redirect.PageName);
        var entry = Assert.Single(logger.Entries, e => e.Level == LogLevel.Information);
        Assert.Contains(booking.BookingId.ToString(), entry.Message, StringComparison.Ordinal);
        Assert.Contains(inspector.DisplayName, entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnPostCancelAsync_SuccessfulCancellation_LogsInformationWithBookingId()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        var booking = await fixture.SeedBookingAsync("Unassigned");

        await using var db = fixture.CreateContext();
        var logger = new ListLogger<IndexModel>();
        var model = CreateModel(db, logger);

        var result = await model.OnPostCancelAsync(booking.BookingId);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Null(redirect.PageName);
        var entry = Assert.Single(logger.Entries, e => e.Level == LogLevel.Information);
        Assert.Contains(booking.BookingId.ToString(), entry.Message, StringComparison.Ordinal);
    }

    private static IndexModel CreateModel(InspectionsContext db, ListLogger<IndexModel> logger)
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
            logger);

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
            var fixture = new SqlServerFixture("KorAdminIndexLoggingTests_" + Guid.NewGuid().ToString("N"));
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

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }

        public sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
