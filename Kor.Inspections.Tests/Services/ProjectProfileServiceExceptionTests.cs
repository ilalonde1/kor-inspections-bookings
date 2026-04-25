using Kor.Inspections.App.Data;
using Kor.Inspections.App.Services;
using Microsoft.EntityFrameworkCore;

namespace Kor.Inspections.Tests.Services;

public class ProjectProfileServiceExceptionTests
{
    [Fact]
    public async Task AddOrUpdateContactAsync_EmptyEmail_ThrowsInvalidContactEmailException()
    {
        await using var db = CreateContext();
        var service = new ProjectProfileService(db);

        await Assert.ThrowsAsync<InvalidContactEmailException>(() =>
            service.AddOrUpdateContactAsync(
                null,
                "30844",
                "requester@example.com",
                "Contact Name",
                "6045551212",
                "",
                null));
    }

    [Fact]
    public async Task AddOrUpdateContactAsync_InvalidEmail_ThrowsInvalidContactEmailException()
    {
        await using var db = CreateContext();
        var service = new ProjectProfileService(db);

        await Assert.ThrowsAsync<InvalidContactEmailException>(() =>
            service.AddOrUpdateContactAsync(
                null,
                "30844",
                "requester@example.com",
                "Contact Name",
                "6045551212",
                "notanemail",
                null));
    }

    [Fact]
    public async Task AddOrUpdateContactAsync_MissingContactId_ThrowsContactNotFoundException()
    {
        await using var db = CreateContext();
        var service = new ProjectProfileService(db);

        await Assert.ThrowsAsync<ContactNotFoundException>(() =>
            service.AddOrUpdateContactAsync(
                999,
                "30844",
                "requester@example.com",
                "Contact Name",
                "6045551212",
                "contact@example.com",
                null));
    }

    private static InspectionsContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<InspectionsContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new InspectionsContext(options);
    }
}
