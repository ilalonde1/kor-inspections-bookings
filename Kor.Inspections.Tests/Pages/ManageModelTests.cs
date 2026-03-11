using Kor.Inspections.App.Data;
using Kor.Inspections.App.Data.Models;
using Kor.Inspections.App.Options;
using Kor.Inspections.App.Pages;
using Kor.Inspections.App.Services;
using Kor.Inspections.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Inspections.Tests.Pages;

public class ManageModelTests
{
    [Fact]
    public async Task OnPostAsync_CompletedBooking_DoesNotChangeStatusOrSendEmails()
    {
        await using var db = CreateContext();
        var booking = await AddBookingAsync(db, "Completed");
        var logger = new ListLogger<BookingService>();
        var model = CreateModel(db, logger, out _);
        model.Token = booking.CancelToken;

        var result = await model.OnPostAsync();

        var updated = await db.Bookings.SingleAsync(b => b.BookingId == booking.BookingId);
        Assert.IsType<PageResult>(result);
        Assert.Equal("Completed", updated.Status);
        Assert.Empty(db.BookingActions);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task OnPostAsync_CancelledBooking_DoesNotChangeStatusOrSendEmails()
    {
        await using var db = CreateContext();
        var booking = await AddBookingAsync(db, "Cancelled");
        var logger = new ListLogger<BookingService>();
        var model = CreateModel(db, logger, out _);
        model.Token = booking.CancelToken;

        var result = await model.OnPostAsync();

        var updated = await db.Bookings.SingleAsync(b => b.BookingId == booking.BookingId);
        Assert.IsType<PageResult>(result);
        Assert.Equal("Cancelled", updated.Status);
        Assert.Empty(db.BookingActions);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task OnPostAsync_OpenCancellationWindow_ChangesStatusToCancelled()
    {
        await using var db = CreateContext();
        var booking = await AddBookingAsync(db, "Unassigned");
        var logger = new ListLogger<BookingService>();
        var model = CreateModel(db, logger, out _);
        model.Token = booking.CancelToken;

        var result = await model.OnPostAsync();

        var updated = await db.Bookings.SingleAsync(b => b.BookingId == booking.BookingId);
        Assert.IsType<PageResult>(result);
        Assert.Equal("Cancelled", updated.Status);
        var action = Assert.Single(db.BookingActions);
        Assert.Equal("Cancelled", action.ActionType);
        Assert.Equal("client-token", action.PerformedBy);
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Error &&
                     entry.Message.Contains("Failed to send cancellation emails", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OnPostAsync_DoublePost_WritesOneAuditRecord()
    {
        await using var db = CreateContext();
        var booking = await AddBookingAsync(db, "Unassigned");
        var logger = new ListLogger<BookingService>();
        var model = CreateModel(db, logger, out _);
        model.Token = booking.CancelToken;

        var firstResult = await model.OnPostAsync();
        var secondResult = await model.OnPostAsync();

        var updated = await db.Bookings.SingleAsync(b => b.BookingId == booking.BookingId);
        var action = Assert.Single(db.BookingActions);
        Assert.IsType<PageResult>(firstResult);
        Assert.IsType<PageResult>(secondResult);
        Assert.Equal("Cancelled", updated.Status);
        Assert.Equal("Cancelled", action.ActionType);
        Assert.Equal("client-token", action.PerformedBy);
    }

    [Fact]
    public async Task OnGetAsync_CancelledBookingWithClosedWindow_SetsTerminalStateAndShowsProject()
    {
        await using var db = CreateContext();
        var booking = await AddBookingAsync(db, "Cancelled", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);
        var logger = new ListLogger<BookingService>();
        var model = CreateModel(db, logger, out _);
        model.Token = booking.CancelToken;

        await model.OnGetAsync();

        Assert.True(model.IsTerminalState);
        Assert.Equal("30844", model.ProjectNumber);
    }

    [Fact]
    public async Task OnGetAsync_CompletedBookingWithClosedWindow_SetsTerminalState()
    {
        await using var db = CreateContext();
        var booking = await AddBookingAsync(db, "Completed", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);
        var logger = new ListLogger<BookingService>();
        var model = CreateModel(db, logger, out _);
        model.Token = booking.CancelToken;

        await model.OnGetAsync();

        Assert.True(model.IsTerminalState);
    }

    [Fact]
    public async Task OnGetAsync_CancelledBookingWithOpenWindow_RemainsVisible()
    {
        await using var db = CreateContext();
        var booking = await AddBookingAsync(db, "Cancelled");
        var logger = new ListLogger<BookingService>();
        var model = CreateModel(db, logger, out _);
        model.Token = booking.CancelToken;

        await model.OnGetAsync();

        Assert.False(model.IsTerminalState);
        Assert.Equal("30844", model.ProjectNumber);
    }

    [Fact]
    public async Task OnGetAsync_UnassignedFutureBooking_IsNotTerminal()
    {
        await using var db = CreateContext();
        var booking = await AddBookingAsync(db, "Unassigned");
        var logger = new ListLogger<BookingService>();
        var model = CreateModel(db, logger, out _);
        model.Token = booking.CancelToken;

        await model.OnGetAsync();

        Assert.False(model.IsTerminalState);
    }

    private static ManageModel CreateModel(
        InspectionsContext db,
        ListLogger<BookingService> bookingLogger,
        out ListLogger<ManageModel> manageLogger)
    {
        var timeZone = TimeRuleServiceTestFactory.FindZone(nowLocal =>
            nowLocal.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday &&
            nowLocal.Hour <= 22);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);
        var timeRules = TimeRuleServiceTestFactory.Create(timeZone, nowLocal.Hour + 1);

        var graphMail = new GraphMailService(new ThrowingTokenProvider(), new NoOpHttpClientFactory());
        var bookingService = new BookingService(
            db,
            Options.Create(new NotificationOptions
            {
                FromMailbox = "reviews@example.com",
                Email = "reviews@example.com",
                DisplayName = "KOR Reviews"
            }),
            bookingLogger,
            timeRules,
            graphMail,
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

        manageLogger = new ListLogger<ManageModel>();
        var model = new ManageModel(db, timeRules, bookingService);
        model.PageContext = new PageContext
        {
            ViewData = new ViewDataDictionary(
                new EmptyModelMetadataProvider(),
                new ModelStateDictionary())
        };
        return model;
    }

    private static async Task<Booking> AddBookingAsync(
        InspectionsContext db,
        string status,
        DateTime? startUtc = null,
        DateTime? endUtc = null)
    {
        var resolvedStartUtc = startUtc ?? DateTime.UtcNow.AddDays(2);
        var resolvedEndUtc = endUtc ?? resolvedStartUtc.AddHours(1);

        var booking = new Booking
        {
            BookingId = Guid.NewGuid(),
            CancelToken = Guid.NewGuid(),
            ProjectNumber = "30844",
            ProjectAddress = "123 Test St",
            ContactName = "Jane Doe",
            ContactPhone = "6045551212",
            ContactEmail = "jane@example.com",
            StartUtc = resolvedStartUtc,
            EndUtc = resolvedEndUtc,
            Status = status,
            CreatedUtc = DateTime.UtcNow
        };

        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return booking;
    }

    private static InspectionsContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<InspectionsContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new InspectionsContext(options);
    }

    private sealed class NoOpHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class ThrowingTokenProvider : IGraphTokenProvider
    {
        public Task<string> GetTokenAsync()
        {
            throw new InvalidOperationException("Missing Graph configuration.");
        }
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
