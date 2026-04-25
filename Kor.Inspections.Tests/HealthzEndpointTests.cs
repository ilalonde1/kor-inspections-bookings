using Kor.Inspections.App.Data;
using Kor.Inspections.App.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Net;

namespace Kor.Inspections.Tests;

public class HealthzEndpointTests
{
    private const string TestProbeKey = "test-probe-key-value";

    [Fact]
    public async Task GetHealthz_WithNoProbeKey_ReturnsUnauthorized()
    {
        await using var factory = new HealthzWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetHealthz_WithValidProbeKey_ReturnsHealthy()
    {
        await using var factory = new HealthzWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add(
            HealthProbeAuthenticationHandler.HeaderName,
            TestProbeKey);

        var response = await client.GetAsync("/healthz");
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal("Healthy", body);
    }

    [Fact]
    public async Task GetHealthz_WithInvalidProbeKey_ReturnsUnauthorized()
    {
        await using var factory = new HealthzWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add(
            HealthProbeAuthenticationHandler.HeaderName,
            "definitely-not-the-key");

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed class HealthzWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");

            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new[]
                {
                    new KeyValuePair<string, string?>("Health:ProbeKey", TestProbeKey)
                });
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<InspectionsContext>));
                services.RemoveAll(typeof(InspectionsContext));
                services.AddDbContext<InspectionsContext>(options =>
                    options.UseInMemoryDatabase("healthz-" + Guid.NewGuid().ToString("N")));

                // Tests don't have a real SQL Server. Drop the SqlServer healthcheck
                // so the endpoint reports Healthy based on an empty (always-passing) check set.
                services.RemoveAll(typeof(HealthCheckService));
                services.AddHealthChecks();

                using var scope = services.BuildServiceProvider().CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<InspectionsContext>();
                db.Database.EnsureCreated();
            });
        }
    }
}
