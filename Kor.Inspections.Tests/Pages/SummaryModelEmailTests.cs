using System.Net;
using Kor.Inspections.App.Data;
using Kor.Inspections.App.Data.Models;
using Kor.Inspections.App.Options;
using Kor.Inspections.App.Pages.Admin;
using Kor.Inspections.App.Services;
using Kor.Inspections.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Kor.Inspections.Tests.Pages;

public class SummaryModelEmailTests
{
    [Fact]
    public async Task OnPostEmailAsync_WhenMailFails_SetsFriendlyStatusMessage()
    {
        await using var db = CreateContext();
        await SeedBookingAsync(db, assignedTo: null);

        var model = CreateModel(db, new ThrowingHttpClientFactory());

        var result = await model.OnPostEmailAsync();

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Unable to send the full summary. Please try again.", model.StatusMessage);
    }

    [Fact]
    public async Task OnPostEmailInspectorAsync_WhenMailFails_SetsFriendlyStatusMessage()
    {
        await using var db = CreateContext();
        await SeedInspectorAsync(db, "inspector@example.com", "Inspector One");
        await SeedBookingAsync(db, assignedTo: "inspector@example.com");

        var model = CreateModel(db, new ThrowingHttpClientFactory());

        var result = await model.OnPostEmailInspectorAsync("inspector@example.com");

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Unable to send the inspector summary for inspector@example.com. Please try again.", model.StatusMessage);
    }

    [Fact]
    public async Task OnPostEmailAllInspectorsAsync_WhenOneMailFails_ReportsSentAndFailedRecipients()
    {
        await using var db = CreateContext();
        await SeedInspectorAsync(db, "ok@example.com", "Inspector OK");
        await SeedInspectorAsync(db, "fail@example.com", "Inspector Fail");
        await SeedBookingAsync(db, assignedTo: "ok@example.com");
        await SeedBookingAsync(db, assignedTo: "fail@example.com");

        var model = CreateModel(db, new SelectiveFailureHttpClientFactory("fail@example.com"));

        var result = await model.OnPostEmailAllInspectorsAsync();

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Contains("ok@example.com", model.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fail@example.com", model.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Failed", model.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnPostEmailInspectorAsync_IncludesGoogleMapsRouteLink()
    {
        await using var db = CreateContext();
        await SeedInspectorAsync(db, "inspector@example.com", "Inspector One");
        await SeedBookingAsync(db, assignedTo: "inspector@example.com");
        var captureFactory = new CaptureHttpClientFactory();

        var model = CreateModel(db, captureFactory);

        var result = await model.OnPostEmailInspectorAsync("inspector@example.com");

        Assert.IsType<RedirectToPageResult>(result);
        Assert.NotNull(captureFactory.LastRequestBody);
        Assert.Contains("Open inspector route in Google Maps", captureFactory.LastRequestBody, StringComparison.Ordinal);
        Assert.Contains(
            "https://www.google.com/maps/dir/?api=1&amp;origin=123%20Test%20St&amp;destination=123%20Test%20St",
            captureFactory.LastRequestBody,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnPostSaveRouteOrderAsync_PersistsDraggedOrder()
    {
        await using var db = CreateContext();
        await SeedInspectorAsync(db, "inspector@example.com", "Inspector One");
        var first = await SeedBookingAsync(db, "inspector@example.com", "123 Test St", 16);
        var second = await SeedBookingAsync(db, "inspector@example.com", "456 Route Ave", 17);

        var model = CreateModel(db, new CaptureHttpClientFactory());

        var result = await model.OnPostSaveRouteOrderAsync(new SummaryModel.RouteOrderRequest
        {
            InspectorEmail = "inspector@example.com",
            OrderedBookingIds = $"{second.BookingId},{first.BookingId}"
        });

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Route order saved.", model.StatusMessage);

        var persisted = await db.Bookings
            .OrderBy(b => b.RouteOrder)
            .Select(b => new { b.BookingId, b.RouteOrder })
            .ToListAsync();

        Assert.Collection(
            persisted,
            row =>
            {
                Assert.Equal(second.BookingId, row.BookingId);
                Assert.Equal(0, row.RouteOrder);
            },
            row =>
            {
                Assert.Equal(first.BookingId, row.BookingId);
                Assert.Equal(1, row.RouteOrder);
            });
    }

    [Fact]
    public async Task OnPostEmailRouteAsync_UsesDraggedOrderForGoogleMapsLink()
    {
        await using var db = CreateContext();
        await SeedInspectorAsync(db, "inspector@example.com", "Inspector One");
        var first = await SeedBookingAsync(db, "inspector@example.com", "123 Test St", 16);
        var second = await SeedBookingAsync(db, "inspector@example.com", "456 Route Ave", 17);
        var captureFactory = new CaptureHttpClientFactory();

        var model = CreateModel(db, captureFactory);

        var result = await model.OnPostEmailRouteAsync(new SummaryModel.RouteOrderRequest
        {
            InspectorEmail = "inspector@example.com",
            OrderedBookingIds = $"{second.BookingId},{first.BookingId}"
        });

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Inspector summary sent to inspector@example.com.", model.StatusMessage);
        Assert.NotNull(captureFactory.LastRequestBody);
        Assert.Contains(
            "https://www.google.com/maps/dir/?api=1&amp;origin=456%20Route%20Ave&amp;destination=123%20Test%20St",
            captureFactory.LastRequestBody,
            StringComparison.Ordinal);
    }

    private static SummaryModel CreateModel(InspectionsContext db, IHttpClientFactory httpClientFactory)
    {
        var timeZone = TimeRuleServiceTestFactory.FindZone(nowLocal =>
            nowLocal.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday &&
            nowLocal.Hour <= 22);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);
        var timeRules = TimeRuleServiceTestFactory.Create(timeZone, nowLocal.Hour + 1);

        return new SummaryModel(
            db,
            timeRules,
            new GraphMailService(new FixedTokenProvider(), httpClientFactory),
            Options.Create(new NotificationOptions
            {
                FromMailbox = "reviews@example.com",
                Email = "reviews@example.com",
                DisplayName = "KOR Reviews"
            }),
            NullLogger<SummaryModel>.Instance);
    }

    private static async Task SeedInspectorAsync(InspectionsContext db, string email, string displayName)
    {
        db.Inspectors.Add(new Inspector
        {
            Email = email,
            DisplayName = displayName,
            Enabled = true,
            DailyMax = 8
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Booking> SeedBookingAsync(
        InspectionsContext db,
        string? assignedTo,
        string projectAddress = "123 Test St",
        int startHourUtc = 16)
    {
        var booking = new Booking
        {
            BookingId = Guid.NewGuid(),
            CancelToken = Guid.NewGuid(),
            ProjectNumber = "30844",
            ProjectAddress = projectAddress,
            ContactName = "Jane Doe",
            ContactPhone = "6045551212",
            ContactEmail = "jane@example.com",
            StartUtc = DateTime.UtcNow.AddDays(1).Date.AddHours(startHourUtc),
            EndUtc = DateTime.UtcNow.AddDays(1).Date.AddHours(startHourUtc + 1),
            Status = assignedTo is null ? "Unassigned" : "Assigned",
            AssignedTo = assignedTo,
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

    private sealed class FixedTokenProvider : IGraphTokenProvider
    {
        public Task<string> GetTokenAsync() => Task.FromResult("test-token");
    }

    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new ThrowingHandler());
    }

    private sealed class SelectiveFailureHttpClientFactory(string failingEmail) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new SelectiveFailureHandler(failingEmail));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromException<HttpResponseMessage>(new HttpRequestException("Simulated mail failure."));
        }
    }

    private sealed class SelectiveFailureHandler(string failingEmail) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (body.Contains(failingEmail, StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("Simulated send failure.")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }
    }

    private sealed class CaptureHttpClientFactory : IHttpClientFactory
    {
        public string? LastRequestBody { get; private set; }

        public HttpClient CreateClient(string name) => new(new CaptureHandler(this));

        private sealed class CaptureHandler(CaptureHttpClientFactory owner) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                owner.LastRequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }
        }
    }
}
