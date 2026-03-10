using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Identity.Client;

namespace Kor.Inspections.App.Services
{
    public class GraphMailService
    {
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _httpClientFactory;

        public GraphMailService(IConfiguration config, IHttpClientFactory httpClientFactory)
        {
            _config = config;
            _httpClientFactory = httpClientFactory;
        }

        private async Task<string> GetTokenAsync()
        {
            var tenantId = _config["Graph:TenantId"];
            var clientId = _config["Graph:ClientId"];
            var clientSecret = _config["Graph:ClientSecret"];

            if (string.IsNullOrWhiteSpace(tenantId) ||
                string.IsNullOrWhiteSpace(clientId) ||
                string.IsNullOrWhiteSpace(clientSecret))
            {
                throw new InvalidOperationException(
                    "Missing Graph configuration. Expected Graph:TenantId, Graph:ClientId, Graph:ClientSecret in appsettings.json.");
            }

            var app = ConfidentialClientApplicationBuilder
                .Create(clientId)
                .WithClientSecret(clientSecret)
                .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
                .Build();

            var result = await app
                .AcquireTokenForClient(new[] { "https://graph.microsoft.com/.default" })
                .ExecuteAsync();

            return result.AccessToken;
        }

        public async Task SendHtmlAsync(string fromUserPrincipalName, string toEmail, string subject, string htmlBody)
        {
            var token = await GetTokenAsync();

            var payload = new
            {
                message = new
                {
                    subject,
                    body = new
                    {
                        contentType = "HTML",
                        content = htmlBody
                    },
                    toRecipients = new[]
                    {
                        new
                        {
                            emailAddress = new
                            {
                                address = toEmail
                            }
                        }
                    }
                },
                saveToSentItems = true
            };

            var json = JsonSerializer.Serialize(payload);

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var url = $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(fromUserPrincipalName)}/sendMail";

            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException(
                    $"Graph sendMail failed: {(int)response.StatusCode} {response.ReasonPhrase}. {errorText}");
            }
        }
    }
}
