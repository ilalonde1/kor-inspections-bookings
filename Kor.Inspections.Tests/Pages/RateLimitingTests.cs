using Kor.Inspections.App.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Kor.Inspections.Tests.Pages;

/// <summary>
/// End-to-end proof that [EnableRateLimiting] on page handler methods is
/// actually enforced. Before PageHandlerRateLimitFilter existed, the built-in
/// middleware ignored handler-level attributes (Razor Pages are one endpoint
/// per page), so these limits were silently inert — verified empirically:
/// 14 rapid requests to the 10/10min verification handler all returned 200.
/// </summary>
public class RateLimitingTests
{
    [Fact]
    public async Task VerificationHandler_EleventhRapidRequest_Returns429WithJsonError()
    {
        await using var factory = new RateLimitWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false
        });

        var token = await GetAntiforgeryTokenAsync(client);

        var statuses = new List<HttpStatusCode>();
        string lastBody = string.Empty;
        for (var i = 0; i < 11; i++)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post, "/?handler=ProjectEmailVerificationStatus");
            request.Headers.Add("RequestVerificationToken", token);
            request.Content = new StringContent(
                "{\"projectNumber\":\"\",\"email\":\"\"}", Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request);
            statuses.Add(response.StatusCode);
            lastBody = await response.Content.ReadAsStringAsync();
        }

        // The "verification" policy allows 10 requests per 10-minute window.
        Assert.All(statuses.Take(10), s => Assert.Equal(HttpStatusCode.OK, s));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
        // Body must be JSON with an error field so client-side readJsonSafe /
        // describeHttpError keep working.
        Assert.Contains("Too many requests", lastBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnlimitedHandler_IsNotThrottled()
    {
        await using var factory = new RateLimitWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false
        });

        // OnGetAsync has no [EnableRateLimiting]; repeated loads must all succeed.
        for (var i = 0; i < 12; i++)
        {
            var response = await client.GetAsync("/");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var homepage = await client.GetAsync("/");
        homepage.EnsureSuccessStatusCode();
        var html = await homepage.Content.ReadAsStringAsync();

        var match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
            RegexOptions.IgnoreCase);
        Assert.True(match.Success, "Antiforgery token not found on the booking page.");
        return match.Groups[1].Value;
    }

    private sealed class RateLimitWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<InspectionsContext>));
                services.RemoveAll(typeof(InspectionsContext));
                services.AddDbContext<InspectionsContext>(options =>
                    options.UseInMemoryDatabase("ratelimit-" + Guid.NewGuid().ToString("N")));

                using var scope = services.BuildServiceProvider().CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<InspectionsContext>();
                db.Database.EnsureCreated();
            });
        }
    }
}
