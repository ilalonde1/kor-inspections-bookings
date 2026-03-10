using Kor.Inspections.App.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kor.Inspections.Tests;

public class HealthzEndpointTests
{
    [Fact]
    public async Task GetHealthz_WhenDatabaseReachable_ReturnsHealthy()
    {
        await using var factory = new HealthzWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost")
        });

        var response = await client.GetAsync("/healthz");
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal("Healthy", body);
    }

    private sealed class HealthzWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<InspectionsContext>));
                services.RemoveAll(typeof(InspectionsContext));
                services.AddDbContext<InspectionsContext>(options =>
                    options.UseInMemoryDatabase("healthz-" + Guid.NewGuid().ToString("N")));

                using var scope = services.BuildServiceProvider().CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<InspectionsContext>();
                db.Database.EnsureCreated();
            });
        }
    }
}
