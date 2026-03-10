using Kor.Inspections.App.Data;
using Kor.Inspections.App.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kor.Inspections.Tests.Services;

public class ProjectAccessServiceTests
{
    [Fact]
    public async Task ValidatePinAsync_ValidEnabledProject_ReturnsTrue()
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        await service.SetOrUpdatePinAsync("30844", "1234");

        var result = await service.ValidatePinAsync("30844", "1234");

        Assert.True(result);
    }

    [Fact]
    public async Task ValidatePinAsync_IncorrectPin_ReturnsFalse()
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        await service.SetOrUpdatePinAsync("30844", "1234");

        var result = await service.ValidatePinAsync("30844", "9999");

        Assert.False(result);
    }

    [Fact]
    public async Task ValidatePinAsync_DisabledProject_ReturnsFalse()
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        var access = await service.SetOrUpdatePinAsync("30844", "1234");
        access.IsEnabled = false;
        await db.SaveChangesAsync();

        var result = await service.ValidatePinAsync("30844", "1234");

        Assert.False(result);
    }

    [Fact]
    public async Task ValidatePinAsync_ProjectNotFound_ReturnsFalse()
    {
        await using var db = CreateContext();
        var service = CreateService(db);

        var result = await service.ValidatePinAsync("99999", "1234");

        Assert.False(result);
    }

    [Fact]
    public async Task ValidatePinAsync_EqualLengthIncorrectPin_ReturnsFalse()
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        await service.SetOrUpdatePinAsync("30844", "1234");

        var result = await service.ValidatePinAsync("30844", "1235");

        Assert.False(result);
    }

    private static InspectionsContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<InspectionsContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new InspectionsContext(options);
    }

    private static ProjectAccessService CreateService(InspectionsContext db)
    {
        return new ProjectAccessService(
            db,
            NullLogger<ProjectAccessService>.Instance);
    }
}
