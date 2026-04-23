using Kor.Inspections.App.Data;
using Kor.Inspections.App.Data.Models;
using Kor.Inspections.App.Options;
using Kor.Inspections.App.Pages.Admin;
using Kor.Inspections.App.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Kor.Inspections.Tests.Pages;

public class TrustedDomainsModelTests
{
    [Fact]
    public async Task OnGetAsync_NoRows_ReturnsEmptyList()
    {
        await using var db = CreateContext();
        var model = new TrustedDomainsModel(db);

        await model.OnGetAsync();

        Assert.Empty(model.TrustedDomains);
    }

    [Fact]
    public async Task OnGetAsync_LoadsRowsOrderedByProjectThenDomain()
    {
        await using var db = CreateContext();
        db.ProjectDefaults.AddRange(
            new ProjectDefault { ProjectNumber = "30845", EmailDomain = "beta.com", UpdatedUtc = DateTime.UtcNow },
            new ProjectDefault { ProjectNumber = "30844", EmailDomain = "zeta.com", UpdatedUtc = DateTime.UtcNow },
            new ProjectDefault { ProjectNumber = "30844", EmailDomain = "alpha.com", UpdatedUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var model = new TrustedDomainsModel(db);

        await model.OnGetAsync();

        Assert.Equal(3, model.TrustedDomains.Count);
        Assert.Collection(
            model.TrustedDomains,
            row =>
            {
                Assert.Equal("30844", row.ProjectNumber);
                Assert.Equal("alpha.com", row.EmailDomain);
            },
            row =>
            {
                Assert.Equal("30844", row.ProjectNumber);
                Assert.Equal("zeta.com", row.EmailDomain);
            },
            row =>
            {
                Assert.Equal("30845", row.ProjectNumber);
                Assert.Equal("beta.com", row.EmailDomain);
            });
    }

    [Fact]
    public async Task OnPostRevokeAsync_ValidId_DeletesRowRemovesCacheAndSetsStatusMessage()
    {
        await using var db = CreateContext();
        var row = new ProjectDefault
        {
            ProjectNumber = "30844",
            EmailDomain = "acme.com",
            UpdatedUtc = DateTime.UtcNow
        };
        db.ProjectDefaults.Add(row);
        await db.SaveChangesAsync();

        var model = new TrustedDomainsModel(db);

        var result = await model.OnPostRevokeAsync(row.Id);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Empty(db.ProjectDefaults);
        Assert.Contains("Revoked explicit domain approval", model.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnPostRevokeAsync_MissingId_SetsNotFoundStatusMessage()
    {
        await using var db = CreateContext();
        var model = new TrustedDomainsModel(db);

        var result = await model.OnPostRevokeAsync(999);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Trusted domain was not found.", model.StatusMessage);
    }

    [Fact]
    public async Task RevokeThenGetStatusAsync_ReturnsNotVerified()
    {
        await using var db = CreateContext();
        var row = new ProjectDefault
        {
            ProjectNumber = "30844",
            EmailDomain = "acme.com",
            UpdatedUtc = DateTime.UtcNow
        };
        db.ProjectDefaults.Add(row);
        await db.SaveChangesAsync();

        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var model = new TrustedDomainsModel(db);
        await model.OnPostRevokeAsync(row.Id);

        var service = CreateVerificationService(db, cache);
        var status = await service.GetStatusAsync("30844", "user@acme.com");

        Assert.True(status.RequiresVerification);
        Assert.False(status.IsVerified);
    }

    [Fact]
    public async Task OnGetAsync_ExpiredApproval_IsMarkedExpired()
    {
        await using var db = CreateContext();
        db.ProjectDefaults.Add(new ProjectDefault
        {
            ProjectNumber = "30844",
            EmailDomain = "expired.com",
            UpdatedUtc = DateTime.UtcNow - TimeSpan.FromDays(30) - TimeSpan.FromMinutes(1)
        });
        await db.SaveChangesAsync();

        var model = new TrustedDomainsModel(db);

        await model.OnGetAsync();

        var row = Assert.Single(model.TrustedDomains);
        Assert.True(row.IsExpired);
        Assert.True(row.ExpiresUtc < DateTime.UtcNow);
    }

    private static ProjectBootstrapVerificationService CreateVerificationService(InspectionsContext db, Microsoft.Extensions.Caching.Memory.IMemoryCache cache)
    {
        return new ProjectBootstrapVerificationService(
            cache,
            new GraphMailService(new ThrowingTokenProvider(), new NoOpHttpClientFactory()),
            Options.Create(new NotificationOptions
            {
                FromMailbox = "reviews@example.com",
                Email = "reviews@example.com",
                DisplayName = "KOR Reviews"
            }),
            db,
            NullLogger<ProjectBootstrapVerificationService>.Instance);
    }

    private static InspectionsContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<InspectionsContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new InspectionsContext(options);
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
