using Kor.Inspections.App.Data;
using Kor.Inspections.App.Data.Models;
using Kor.Inspections.App.Options;
using Kor.Inspections.App.Services;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Kor.Inspections.Tests.Services;

/// <summary>
/// Regression coverage for the DB-backed verification state refactor
/// (Codex finding #1). Verifies that verified state survives a fresh
/// service + DbContext instance (simulating process recycle), that
/// expired rows aren't trusted, and that the failed-attempt lockout
/// still works.
/// </summary>
public class ProjectBootstrapVerificationServiceTests
{
    private const string ProjectNumber = "30844";
    private const string Email = "user@acme.com";

    [Fact]
    public async Task VerifyCodeAsync_ThenGetStatus_WithFreshService_StillReportsVerified()
    {
        // Simulates app-pool recycle: write the verified state with one
        // DbContext + service, then construct fresh instances of both and
        // confirm IsVerified is still true.
        var dbName = Guid.NewGuid().ToString("N");

        await using (var db = CreateContext(dbName))
        {
            var service = CreateService(db);
            // SendCodeAsync persists state before attempting the mail send;
            // the mock token provider throws so the mail path returns false,
            // but the DB row is intact. Assert the row, not the return value.
            await service.SendCodeAsync(ProjectNumber, Email);

            var code = (await db.ProjectVerifications.SingleAsync()).Code;
            Assert.True(await service.VerifyCodeAsync(ProjectNumber, Email, code));
        }

        // New scope — mimics a fresh request after a recycle.
        await using (var freshDb = CreateContext(dbName))
        {
            var freshService = CreateService(freshDb);
            var status = await freshService.GetStatusAsync(ProjectNumber, Email);

            Assert.True(status.RequiresVerification);
            Assert.True(status.IsVerified);
        }
    }

    [Fact]
    public async Task VerifyCodeAsync_WhenRowIsExpired_ReturnsFalseAndDoesNotTrust()
    {
        var dbName = Guid.NewGuid().ToString("N");
        await using var db = CreateContext(dbName);

        // Seed a row that was verified in the past but has already expired.
        db.ProjectVerifications.Add(new ProjectVerification
        {
            ProjectNumber = ProjectNumber,
            Email = Email,
            Code = "123456",
            Verified = true,
            FailedAttempts = 0,
            ExpiresUtc = DateTime.UtcNow.AddMinutes(-1),
            CreatedUtc = DateTime.UtcNow.AddHours(-9),
            UpdatedUtc = DateTime.UtcNow.AddHours(-9)
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var status = await service.GetStatusAsync(ProjectNumber, Email);

        Assert.True(status.RequiresVerification);
        Assert.False(status.IsVerified);
    }

    [Fact]
    public async Task VerifyCodeAsync_FiveWrongAttempts_ExpiresRowSoFurtherAttemptsFail()
    {
        var dbName = Guid.NewGuid().ToString("N");
        await using var db = CreateContext(dbName);

        var service = CreateService(db);
        // SendCodeAsync persists state before attempting mail; the mock mail
        // path throws so the overall return is false, but the DB row is set.
        await service.SendCodeAsync(ProjectNumber, Email);
        var correctCode = (await db.ProjectVerifications.AsNoTracking().SingleAsync()).Code;

        // Five wrong attempts lock out the row by expiring ExpiresUtc.
        for (int i = 0; i < 5; i++)
        {
            var ok = await service.VerifyCodeAsync(ProjectNumber, Email, "000000");
            Assert.False(ok);
        }

        // After lockout, even the correct code is rejected.
        var lateAttempt = await service.VerifyCodeAsync(ProjectNumber, Email, correctCode);
        Assert.False(lateAttempt);

        var row = await db.ProjectVerifications.AsNoTracking().SingleAsync();
        Assert.False(row.Verified);
        Assert.Equal(5, row.FailedAttempts);
        Assert.True(row.ExpiresUtc <= DateTime.UtcNow);
    }

    [Fact]
    public async Task SendCodeAsync_AlreadyVerified_IsNoOp_AndDoesNotResetVerifiedState()
    {
        var dbName = Guid.NewGuid().ToString("N");
        await using var db = CreateContext(dbName);

        var service = CreateService(db);
        await service.SendCodeAsync(ProjectNumber, Email);
        var code = (await db.ProjectVerifications.AsNoTracking().SingleAsync()).Code;
        Assert.True(await service.VerifyCodeAsync(ProjectNumber, Email, code));

        // Second SendCodeAsync while already verified must not reset state —
        // the method short-circuits on the already-verified check and returns
        // true without touching the row.
        Assert.True(await service.SendCodeAsync(ProjectNumber, Email));

        var row = await db.ProjectVerifications.AsNoTracking().SingleAsync();
        Assert.True(row.Verified);
        Assert.Equal(0, row.FailedAttempts);
        Assert.Equal(code, row.Code);
    }

    [Fact]
    public async Task SendCodeAsync_TwoConcurrentCallsForSameProjectEmail_BothSucceedAndOnlyOneRowPersists()
    {
        // Sqlite in-memory enforces unique indexes; EF InMemory does not.
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        try
        {
            var options = new DbContextOptionsBuilder<InspectionsContext>()
                .UseSqlite(connection)
                .Options;

            await using (var schemaDb = new InspectionsContext(options))
            {
                await schemaDb.Database.EnsureCreatedAsync();
            }

            // Two separate DbContexts (and services) so they don't share a change tracker -
            // mirrors two concurrent HTTP requests in production.
            await using var dbA = new InspectionsContext(options);
            await using var dbB = new InspectionsContext(options);

            var serviceA = CreateService(dbA);
            var serviceB = CreateService(dbB);

            // Run both calls concurrently; neither should throw.
            var taskA = serviceA.SendCodeAsync(ProjectNumber, Email);
            var taskB = serviceB.SendCodeAsync(ProjectNumber, Email);

            await Task.WhenAll(taskA, taskB);

            Assert.True(taskA.Result);
            Assert.True(taskB.Result);

            // Assert exactly one row persisted.
            await using var verifyDb = new InspectionsContext(options);
            var rows = await verifyDb.ProjectVerifications
                .Where(v => v.ProjectNumber == ProjectNumber && v.Email == Email)
                .ToListAsync();
            Assert.Single(rows);
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    // --------------------------------------------------
    // HELPERS
    // --------------------------------------------------

    private static InspectionsContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<InspectionsContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new InspectionsContext(options);
    }

    private static ProjectBootstrapVerificationService CreateService(InspectionsContext db)
    {
        return new ProjectBootstrapVerificationService(
            new GraphMailService(new SuccessTokenProvider(), new SuccessHttpClientFactory()),
            Options.Create(new NotificationOptions
            {
                FromMailbox = "reviews@example.com",
                Email = "reviews@example.com",
                DisplayName = "KOR Reviews"
            }),
            db,
            NullLogger<ProjectBootstrapVerificationService>.Instance);
    }

    private sealed class SuccessTokenProvider : IGraphTokenProvider
    {
        public Task<string> GetTokenAsync() => Task.FromResult("test-token");
    }

    private sealed class SuccessHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new SuccessHandler());
    }

    private sealed class SuccessHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Accepted));
        }
    }
}
