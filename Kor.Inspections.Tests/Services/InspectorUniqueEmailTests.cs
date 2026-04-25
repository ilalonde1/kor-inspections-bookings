using Kor.Inspections.App.Data;
using Kor.Inspections.App.Data.Models;
using Kor.Inspections.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Kor.Inspections.Tests.Services;

public class InspectorUniqueEmailTests
{
    [Fact]
    public async Task SaveChangesAsync_DuplicateInspectorEmail_ThrowsDbUpdateException()
    {
        await using var fixture = await SqlServerFixture.CreateAsync("KorInspectors_");
        await using var db = fixture.CreateContext();

        db.Inspectors.Add(new Inspector
        {
            DisplayName = "Jane Doe",
            Email = "inspector@example.com",
            Enabled = true
        });
        await db.SaveChangesAsync();

        db.Inspectors.Add(new Inspector
        {
            DisplayName = "John Doe",
            Email = "inspector@example.com",
            Enabled = true
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

}
