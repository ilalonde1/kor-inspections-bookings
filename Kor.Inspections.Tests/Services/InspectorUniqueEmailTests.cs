using Kor.Inspections.App.Data;
using Kor.Inspections.App.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Kor.Inspections.Tests.Services;

public class InspectorUniqueEmailTests
{
    [Fact]
    public async Task SaveChangesAsync_DuplicateInspectorEmail_ThrowsDbUpdateException()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
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

    private sealed class SqlServerFixture : IAsyncDisposable
    {
        private readonly string _connectionString;

        private SqlServerFixture(string databaseName)
        {
            _connectionString = $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True;";
        }

        public static async Task<SqlServerFixture> CreateAsync()
        {
            var fixture = new SqlServerFixture("KorInspectors_" + Guid.NewGuid().ToString("N"));
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
}
