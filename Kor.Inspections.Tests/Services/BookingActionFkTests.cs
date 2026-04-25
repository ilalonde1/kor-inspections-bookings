using Kor.Inspections.App.Data;
using Kor.Inspections.App.Data.Models;
using Kor.Inspections.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Kor.Inspections.Tests.Services;

public class BookingActionFkTests
{
    [Fact]
    public async Task SaveChangesAsync_BookingActionWithInvalidBookingId_ThrowsDbUpdateException()
    {
        await using var fixture = await SqlServerFixture.CreateAsync("KorBookingActionFkTests_");
        await using var db = fixture.CreateContext();

        db.BookingActions.Add(new BookingAction
        {
            BookingId = Guid.NewGuid(),
            ActionType = "Cancelled",
            PerformedBy = "tester@example.com",
            ActionUtc = DateTime.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveChangesAsync_DeleteBooking_CascadesDeleteToBookingActions()
    {
        await using var fixture = await SqlServerFixture.CreateAsync("KorBookingActionFkTests_");
        Guid bookingId;

        await using (var db = fixture.CreateContext())
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
                Status = "Unassigned",
                CreatedUtc = DateTime.UtcNow
            };

            db.Bookings.Add(booking);
            db.BookingActions.Add(new BookingAction
            {
                BookingId = booking.BookingId,
                ActionType = "Created",
                PerformedBy = "tester@example.com",
                ActionUtc = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
            bookingId = booking.BookingId;
        }

        await using (var db = fixture.CreateContext())
        {
            var booking = await db.Bookings.SingleAsync(b => b.BookingId == bookingId);
            db.Bookings.Remove(booking);
            await db.SaveChangesAsync();
        }

        await using (var verifyDb = fixture.CreateContext())
        {
            Assert.Equal(0, await verifyDb.BookingActions.AsNoTracking().CountAsync());
        }
    }

}
