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

public class AdminIndexModelEditBookingTests
{
    [Fact]
    public async Task OnPostEditAsync_NewDateTime_UpdatesBookingAndRecordsAuditRow()
    {
        if (!TryPickWeekdayAfternoonZone(out _, out _)) { Assert.True(true); return; }

        await using var fixture = await SqlServerFixture.CreateAsync("KorAdminEditTests_");
        var (timeZone, nowLocal) = PickWeekdayAfternoonZone();
        var sameAllowedDate = GetAllowedDate(nowLocal, nowLocal.Hour + 1, dayOffset: 0);
        // Seed booking at day+2 local 12:00 as "PM".
        var seededBooking = await SeedBookingAsync(
            fixture,
            status: "Unassigned",
            startUtc: ToUtc(timeZone, sameAllowedDate.ToDateTime(TimeOnly.MinValue), 12, 0),
            endUtc: ToUtc(timeZone, sameAllowedDate.ToDateTime(TimeOnly.MinValue), 16, 0),
            timePreference: "PM");

        await using var db = fixture.CreateContext();
        var model = CreateModel(db, timeZone);

        var result = await model.OnPostEditAsync(seededBooking.BookingId, new IndexModel.EditBookingInput
        {
            RequestedDate = sameAllowedDate.ToDateTime(TimeOnly.MinValue),
            RequestedTime = "AM",
            OverrideCutoff = false
        });

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Booking rescheduled.", model.StatusMessage);

        await using var verify = fixture.CreateContext();
        var updated = await verify.Bookings.AsNoTracking().SingleAsync(b => b.BookingId == seededBooking.BookingId);
        Assert.Equal("AM", updated.TimePreference);
        var newStartLocal = TimeZoneInfo.ConvertTimeFromUtc(updated.StartUtc, timeZone);
        Assert.Equal(sameAllowedDate.ToDateTime(new TimeOnly(8, 0)), newStartLocal);

        var action = Assert.Single(await verify.BookingActions.AsNoTracking().ToListAsync());
        Assert.Equal("Edited", action.ActionType);
        Assert.Contains("From ", action.Notes, StringComparison.Ordinal);
        Assert.Contains("to ", action.Notes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnPostEditAsync_CancelledBooking_BlocksWithoutModifyingRow()
    {
        if (!TryPickWeekdayAfternoonZone(out _, out _)) { Assert.True(true); return; }

        await using var fixture = await SqlServerFixture.CreateAsync("KorAdminEditTests_");
        var (timeZone, nowLocal) = PickWeekdayAfternoonZone();
        var sameAllowedDate = GetAllowedDate(nowLocal, nowLocal.Hour + 1, dayOffset: 0);
        var seededBooking = await SeedBookingAsync(
            fixture,
            status: "Cancelled",
            startUtc: ToUtc(timeZone, sameAllowedDate.ToDateTime(TimeOnly.MinValue), 12, 0),
            endUtc: ToUtc(timeZone, sameAllowedDate.ToDateTime(TimeOnly.MinValue), 13, 0),
            timePreference: null);

        await using var db = fixture.CreateContext();
        var model = CreateModel(db, timeZone);

        var result = await model.OnPostEditAsync(seededBooking.BookingId, new IndexModel.EditBookingInput
        {
            RequestedDate = sameAllowedDate.ToDateTime(TimeOnly.MinValue),
            RequestedTime = "AM",
            OverrideCutoff = false
        });

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Booking cannot be modified.", model.StatusMessage);

        await using var verify = fixture.CreateContext();
        var unchanged = await verify.Bookings.AsNoTracking().SingleAsync(b => b.BookingId == seededBooking.BookingId);
        Assert.Equal(seededBooking.StartUtc, unchanged.StartUtc);
        Assert.Empty(await verify.BookingActions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task OnPostEditAsync_NoChange_SetsNoChangesStatusMessageAndDoesNotWriteAudit()
    {
        if (!TryPickWeekdayAfternoonZone(out _, out _)) { Assert.True(true); return; }

        await using var fixture = await SqlServerFixture.CreateAsync("KorAdminEditTests_");
        var (timeZone, nowLocal) = PickWeekdayAfternoonZone();
        var sameAllowedDate = GetAllowedDate(nowLocal, nowLocal.Hour + 1, dayOffset: 0);
        var seededBooking = await SeedBookingAsync(
            fixture,
            status: "Unassigned",
            startUtc: ToUtc(timeZone, sameAllowedDate.ToDateTime(TimeOnly.MinValue), 8, 0),
            endUtc: ToUtc(timeZone, sameAllowedDate.ToDateTime(TimeOnly.MinValue), 12, 0),
            timePreference: "AM");

        await using var db = fixture.CreateContext();
        var model = CreateModel(db, timeZone);

        var result = await model.OnPostEditAsync(seededBooking.BookingId, new IndexModel.EditBookingInput
        {
            RequestedDate = sameAllowedDate.ToDateTime(TimeOnly.MinValue),
            RequestedTime = "AM",
            OverrideCutoff = false
        });

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("No changes made.", model.StatusMessage);
        Assert.Empty(await db.BookingActions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task OnPostEditAsync_DateChange_NullsOutRouteOrder()
    {
        if (!TryPickWeekdayAfternoonZone(out _, out _)) { Assert.True(true); return; }

        await using var fixture = await SqlServerFixture.CreateAsync("KorAdminEditTests_");
        var (timeZone, nowLocal) = PickWeekdayAfternoonZone();
        var initialAllowedDate = GetAllowedDate(nowLocal, nowLocal.Hour + 1, dayOffset: 0);
        var nextAllowedDate = GetAllowedDate(nowLocal, nowLocal.Hour + 1, dayOffset: 1);
        var seededBooking = await SeedBookingAsync(
            fixture,
            status: "Assigned",
            startUtc: ToUtc(timeZone, initialAllowedDate.ToDateTime(TimeOnly.MinValue), 12, 0),
            endUtc: ToUtc(timeZone, initialAllowedDate.ToDateTime(TimeOnly.MinValue), 16, 0),
            timePreference: "PM",
            routeOrder: 3);

        await using var db = fixture.CreateContext();
        var model = CreateModel(db, timeZone);

        var result = await model.OnPostEditAsync(seededBooking.BookingId, new IndexModel.EditBookingInput
        {
            RequestedDate = nextAllowedDate.ToDateTime(TimeOnly.MinValue),
            RequestedTime = "PM",
            OverrideCutoff = false
        });

        Assert.IsType<RedirectToPageResult>(result);

        await using var verify = fixture.CreateContext();
        var updated = await verify.Bookings.AsNoTracking().SingleAsync(b => b.BookingId == seededBooking.BookingId);
        Assert.Null(updated.RouteOrder);
    }

    [Fact]
    public async Task OnPostEditAsync_TimeOnlySameDayChange_PreservesRouteOrder()
    {
        if (!TryPickWeekdayAfternoonZone(out _, out _)) { Assert.True(true); return; }

        await using var fixture = await SqlServerFixture.CreateAsync("KorAdminEditTests_");
        var (timeZone, nowLocal) = PickWeekdayAfternoonZone();
        var sameAllowedDate = GetAllowedDate(nowLocal, nowLocal.Hour + 1, dayOffset: 0);
        var seededBooking = await SeedBookingAsync(
            fixture,
            status: "Assigned",
            startUtc: ToUtc(timeZone, sameAllowedDate.ToDateTime(TimeOnly.MinValue), 12, 0),
            endUtc: ToUtc(timeZone, sameAllowedDate.ToDateTime(TimeOnly.MinValue), 16, 0),
            timePreference: "PM",
            routeOrder: 5);

        await using var db = fixture.CreateContext();
        var model = CreateModel(db, timeZone);

        var result = await model.OnPostEditAsync(seededBooking.BookingId, new IndexModel.EditBookingInput
        {
            RequestedDate = sameAllowedDate.ToDateTime(TimeOnly.MinValue),
            RequestedTime = "AM",
            OverrideCutoff = false
        });

        Assert.IsType<RedirectToPageResult>(result);

        await using var verify = fixture.CreateContext();
        var updated = await verify.Bookings.AsNoTracking().SingleAsync(b => b.BookingId == seededBooking.BookingId);
        Assert.Equal(5, updated.RouteOrder);
    }

    [Fact]
    public async Task OnPostEditAsync_TargetSlotAtCapacity_BlocksRescheduleWithFriendlyMessage()
    {
        if (!TryPickWeekdayAfternoonZone(out _, out _)) { Assert.True(true); return; }

        await using var fixture = await SqlServerFixture.CreateAsync("KorAdminEditTests_");
        var (timeZone, nowLocal) = PickWeekdayAfternoonZone();

        // Fill day+3 AM (MaxBookingsPerSlot = 3 in the test factory) with 3
        // distinct clients so every AM slot on that day is at capacity.
        var initialAllowedDate = GetAllowedDate(nowLocal, nowLocal.Hour + 1, dayOffset: 0);
        var targetDate = GetAllowedDate(nowLocal, nowLocal.Hour + 1, dayOffset: 1);
        for (int i = 0; i < 3; i++)
        {
            await SeedBookingAsync(
                fixture,
                status: "Unassigned",
                startUtc: ToUtc(timeZone, targetDate.ToDateTime(TimeOnly.MinValue), 8, 0),
                endUtc: ToUtc(timeZone, targetDate.ToDateTime(TimeOnly.MinValue), 12, 0),
                timePreference: "AM",
                contactEmail: $"client{i}@example.com");
        }

        // Booking to edit lives on a different day so it doesn't count toward
        // day+3's AM overlap when it's being moved in.
        var editingBooking = await SeedBookingAsync(
            fixture,
            status: "Assigned",
            startUtc: ToUtc(timeZone, initialAllowedDate.ToDateTime(TimeOnly.MinValue), 12, 0),
            endUtc: ToUtc(timeZone, initialAllowedDate.ToDateTime(TimeOnly.MinValue), 16, 0),
            timePreference: "PM",
            contactEmail: "editing@example.com");

        await using var db = fixture.CreateContext();
        var model = CreateModel(db, timeZone);

        var result = await model.OnPostEditAsync(editingBooking.BookingId, new IndexModel.EditBookingInput
        {
            RequestedDate = targetDate.ToDateTime(TimeOnly.MinValue),
            RequestedTime = "AM",
            OverrideCutoff = false
        });

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Contains("no longer available", model.StatusMessage ?? "", StringComparison.OrdinalIgnoreCase);

        await using var verify = fixture.CreateContext();
        var unchanged = await verify.Bookings.AsNoTracking().SingleAsync(b => b.BookingId == editingBooking.BookingId);
        Assert.Equal(editingBooking.StartUtc, unchanged.StartUtc);
        Assert.Equal("PM", unchanged.TimePreference);
        Assert.Empty(await verify.BookingActions.AsNoTracking()
            .Where(a => a.BookingId == editingBooking.BookingId).ToListAsync());
    }

    [Fact]
    public async Task OnPostEditAsync_ConcurrentModification_SetsConcurrencyStatusMessage()
    {
        if (!TryPickWeekdayAfternoonZone(out _, out _)) { Assert.True(true); return; }

        await using var fixture = await SqlServerFixture.CreateAsync("KorAdminEditTests_");
        var (timeZone, nowLocal) = PickWeekdayAfternoonZone();
        var sameAllowedDate = GetAllowedDate(nowLocal, nowLocal.Hour + 1, dayOffset: 0);
        var seededBooking = await SeedBookingAsync(
            fixture,
            status: "Unassigned",
            startUtc: ToUtc(timeZone, sameAllowedDate.ToDateTime(TimeOnly.MinValue), 12, 0),
            endUtc: ToUtc(timeZone, sameAllowedDate.ToDateTime(TimeOnly.MinValue), 16, 0),
            timePreference: "PM");

        await using var staleContext = fixture.CreateContext();
        _ = await staleContext.Bookings.SingleAsync(b => b.BookingId == seededBooking.BookingId);

        await using (var freshContext = fixture.CreateContext())
        {
            var fresh = await freshContext.Bookings.SingleAsync(b => b.BookingId == seededBooking.BookingId);
            fresh.AssignedTo = "someone@example.com";
            fresh.Status = "Assigned";
            await freshContext.SaveChangesAsync();
        }

        var model = CreateModel(staleContext, timeZone);
        var result = await model.OnPostEditAsync(seededBooking.BookingId, new IndexModel.EditBookingInput
        {
            RequestedDate = sameAllowedDate.ToDateTime(TimeOnly.MinValue),
            RequestedTime = "AM",
            OverrideCutoff = false
        });

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Contains("modified by another user", model.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnPostEditAsync_RescheduleAssignedBooking_UnassignsAndRecordsAudit()
    {
        // Griffin (2026-10-27): a rescheduled inspection goes back to Unassigned.
        if (!TryPickWeekdayAfternoonZone(out _, out _)) { Assert.True(true); return; }

        await using var fixture = await SqlServerFixture.CreateAsync("KorAdminEditTests_");
        var (timeZone, nowLocal) = PickWeekdayAfternoonZone();
        var date = GetAllowedDate(nowLocal, nowLocal.Hour + 1, dayOffset: 0);
        var seeded = await SeedBookingAsync(
            fixture,
            status: "Assigned",
            startUtc: ToUtc(timeZone, date.ToDateTime(TimeOnly.MinValue), 12, 0),
            endUtc: ToUtc(timeZone, date.ToDateTime(TimeOnly.MinValue), 16, 0),
            timePreference: "PM",
            routeOrder: 2,
            assignedTo: "inspector@example.com");

        await using var db = fixture.CreateContext();
        var model = CreateModel(db, timeZone);

        var result = await model.OnPostEditAsync(seeded.BookingId, new IndexModel.EditBookingInput
        {
            RequestedDate = date.ToDateTime(TimeOnly.MinValue),
            RequestedTime = "AM", // same-day time change still counts as a reschedule
            Comments = seeded.Comments
        });

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Contains("Unassigned", model.StatusMessage, StringComparison.Ordinal);

        await using var verify = fixture.CreateContext();
        var updated = await verify.Bookings.AsNoTracking().SingleAsync(b => b.BookingId == seeded.BookingId);
        Assert.Null(updated.AssignedTo);
        Assert.Equal("Unassigned", updated.Status);
        Assert.Null(updated.RouteOrder);

        var actions = await verify.BookingActions.AsNoTracking().ToListAsync();
        Assert.Contains(actions, a => a.ActionType == "Edited" && a.Notes!.StartsWith("From ", StringComparison.Ordinal));
        Assert.Contains(actions, a => a.ActionType == "Unassigned" && a.Notes!.Contains("inspector@example.com", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OnPostEditAsync_DetailsOnly_UpdatesDetailsAndKeepsInspector()
    {
        if (!TryPickWeekdayAfternoonZone(out _, out _)) { Assert.True(true); return; }

        await using var fixture = await SqlServerFixture.CreateAsync("KorAdminEditTests_");
        var (timeZone, nowLocal) = PickWeekdayAfternoonZone();
        var date = GetAllowedDate(nowLocal, nowLocal.Hour + 1, dayOffset: 0);
        var seeded = await SeedBookingAsync(
            fixture,
            status: "Assigned",
            startUtc: ToUtc(timeZone, date.ToDateTime(TimeOnly.MinValue), 12, 0),
            endUtc: ToUtc(timeZone, date.ToDateTime(TimeOnly.MinValue), 16, 0),
            timePreference: "PM",
            routeOrder: 4,
            assignedTo: "inspector@example.com");

        await using var db = fixture.CreateContext();
        var model = CreateModel(db, timeZone);

        var result = await model.OnPostEditAsync(seeded.BookingId, new IndexModel.EditBookingInput
        {
            RequestedDate = date.ToDateTime(TimeOnly.MinValue),
            RequestedTime = "PM",
            Comments = "  Tilt panels #21-#30 on the west casting bed.  "
        });

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Booking details updated.", model.StatusMessage);

        await using var verify = fixture.CreateContext();
        var updated = await verify.Bookings.AsNoTracking().SingleAsync(b => b.BookingId == seeded.BookingId);
        Assert.Equal("Tilt panels #21-#30 on the west casting bed.", updated.Comments);
        Assert.Equal("inspector@example.com", updated.AssignedTo);
        Assert.Equal(4, updated.RouteOrder);
        Assert.Equal(seeded.StartUtc, updated.StartUtc);

        var action = Assert.Single(await verify.BookingActions.AsNoTracking().ToListAsync());
        Assert.Equal("Edited", action.ActionType);
        Assert.Contains("Original details for the review.", action.Notes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnPostEditAsync_DetailsOnlyInsideCutoff_StillSaves()
    {
        // The booking is today (already inside the cutoff window). Editing only
        // the details must not trip the date-window check meant for reschedules.
        if (!TryPickWeekdayAfternoonZone(out _, out _)) { Assert.True(true); return; }

        await using var fixture = await SqlServerFixture.CreateAsync("KorAdminEditTests_");
        var (timeZone, nowLocal) = PickWeekdayAfternoonZone();
        var today = DateOnly.FromDateTime(nowLocal.Date);
        var seeded = await SeedBookingAsync(
            fixture,
            status: "Unassigned",
            startUtc: ToUtc(timeZone, today.ToDateTime(TimeOnly.MinValue), 12, 0),
            endUtc: ToUtc(timeZone, today.ToDateTime(TimeOnly.MinValue), 16, 0),
            timePreference: "PM");

        await using var db = fixture.CreateContext();
        var model = CreateModel(db, timeZone);

        var result = await model.OnPostEditAsync(seeded.BookingId, new IndexModel.EditBookingInput
        {
            RequestedDate = today.ToDateTime(TimeOnly.MinValue),
            RequestedTime = "PM",
            Comments = "Updated details for today's review."
        });

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Booking details updated.", model.StatusMessage);

        await using var verify = fixture.CreateContext();
        var updated = await verify.Bookings.AsNoTracking().SingleAsync(b => b.BookingId == seeded.BookingId);
        Assert.Equal("Updated details for today's review.", updated.Comments);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    public async Task OnPostEditAsync_DetailsTooShort_RejectedWithoutSaving(string comments)
    {
        if (!TryPickWeekdayAfternoonZone(out _, out _)) { Assert.True(true); return; }

        await using var fixture = await SqlServerFixture.CreateAsync("KorAdminEditTests_");
        var (timeZone, nowLocal) = PickWeekdayAfternoonZone();
        var date = GetAllowedDate(nowLocal, nowLocal.Hour + 1, dayOffset: 0);
        var seeded = await SeedBookingAsync(
            fixture,
            status: "Unassigned",
            startUtc: ToUtc(timeZone, date.ToDateTime(TimeOnly.MinValue), 12, 0),
            endUtc: ToUtc(timeZone, date.ToDateTime(TimeOnly.MinValue), 16, 0),
            timePreference: "PM");

        await using var db = fixture.CreateContext();
        var model = CreateModel(db, timeZone);

        var result = await model.OnPostEditAsync(seeded.BookingId, new IndexModel.EditBookingInput
        {
            RequestedDate = date.ToDateTime(TimeOnly.MinValue),
            RequestedTime = "PM",
            Comments = comments
        });

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Contains("between 5 and 500", model.StatusMessage, StringComparison.Ordinal);

        await using var verify = fixture.CreateContext();
        var updated = await verify.Bookings.AsNoTracking().SingleAsync(b => b.BookingId == seeded.BookingId);
        Assert.Equal("Original details for the review.", updated.Comments);
        Assert.Empty(await verify.BookingActions.AsNoTracking().ToListAsync());
    }

    // --------------------------------------------------
    // HELPERS
    // --------------------------------------------------

    private static bool TryPickWeekdayAfternoonZone(out TimeZoneInfo zone, out DateTime nowLocal)
    {
        if (!TimeRuleServiceTestFactory.TryFindZone(local =>
                TimeRuleServiceTestFactory.IsBusinessDay(local.AddDays(1)) &&
                local.Hour <= 22, out zone))
        {
            nowLocal = default;
            return false;
        }
        nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
        return true;
    }

    private static (TimeZoneInfo Zone, DateTime NowLocal) PickWeekdayAfternoonZone()
    {
        if (!TryPickWeekdayAfternoonZone(out var zone, out var nowLocal))
            throw new InvalidOperationException("Test calendar precondition not met; tests must guard with TryPickWeekdayAfternoonZone.");
        return (zone, nowLocal);
    }

    private static DateTime ToUtc(TimeZoneInfo zone, DateTime dateLocal, int hour, int minute)
    {
        var local = new DateTime(dateLocal.Year, dateLocal.Month, dateLocal.Day, hour, minute, 0, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    private static DateOnly GetAllowedDate(DateTime nowLocal, int cutoffHourLocal, int dayOffset)
    {
        var date = DateOnly.FromDateTime(nowLocal.Date)
            .AddDays(nowLocal.Hour < cutoffHourLocal ? 1 : 2);

        while (!TimeRuleServiceTestFactory.IsBusinessDay(date))
            date = date.AddDays(1);

        for (int i = 0; i < dayOffset; i++)
        {
            date = date.AddDays(1);
            while (!TimeRuleServiceTestFactory.IsBusinessDay(date))
                date = date.AddDays(1);
        }

        return date;
    }

    private static IndexModel CreateModel(InspectionsContext db, TimeZoneInfo timeZone)
    {
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);
        var timeRules = TimeRuleServiceTestFactory.Create(timeZone, nowLocal.Hour + 1);

        var bookingService = new BookingService(
            db,
            Options.Create(new NotificationOptions
            {
                FromMailbox = "reviews@example.com",
                AdminRecipientEmail = "reviews@example.com",
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

    private static async Task<Booking> SeedBookingAsync(
        SqlServerFixture fixture,
        string status,
        DateTime startUtc,
        DateTime endUtc,
        string? timePreference,
        int? routeOrder = null,
        string contactEmail = "jane@example.com",
        string? assignedTo = null,
        string? comments = "Original details for the review.")
    {
        var booking = new Booking
        {
            BookingId = Guid.NewGuid(),
            CancelToken = Guid.NewGuid(),
            ProjectNumber = "30844",
            ProjectAddress = "123 Test St",
            ContactName = "Jane Doe",
            ContactPhone = "6045551212",
            ContactEmail = contactEmail,
            StartUtc = startUtc,
            EndUtc = endUtc,
            TimePreference = timePreference,
            RouteOrder = routeOrder,
            Status = status,
            AssignedTo = assignedTo,
            Comments = comments,
            CreatedUtc = DateTime.UtcNow
        };

        await using var db = fixture.CreateContext();
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return booking;
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
