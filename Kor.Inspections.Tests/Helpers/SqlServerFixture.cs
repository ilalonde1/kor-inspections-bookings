using Kor.Inspections.App.Data;
using Microsoft.EntityFrameworkCore;

namespace Kor.Inspections.Tests.Helpers;

public sealed class SqlServerFixture : IAsyncDisposable
{
    private readonly string _connectionString;

    private SqlServerFixture(string databaseName)
    {
        _connectionString =
$"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True;";
    }

    public static async Task<SqlServerFixture> CreateAsync(string databaseNamePrefix)
    {
        var fixture = new SqlServerFixture(databaseNamePrefix + Guid.NewGuid().ToString("N"));
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
