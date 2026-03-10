using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Identity.Client;

namespace Kor.Inspections.App.Services
{
    public interface IGraphTokenProvider
    {
        Task<string> GetTokenAsync();
    }

    public interface IGraphAccessTokenSource
    {
        Task<GraphAccessToken> AcquireTokenAsync();
    }

    public sealed record GraphAccessToken(string AccessToken, DateTimeOffset ExpiresOn);

    public sealed class MsalGraphAccessTokenSource : IGraphAccessTokenSource
    {
        private static readonly string[] GraphScopes = ["https://graph.microsoft.com/.default"];
        private readonly IConfidentialClientApplication _app;

        public MsalGraphAccessTokenSource(IConfiguration config)
        {
            var tenantId = config["Graph:TenantId"];
            var clientId = config["Graph:ClientId"];
            var clientSecret = config["Graph:ClientSecret"];

            if (string.IsNullOrWhiteSpace(tenantId) ||
                string.IsNullOrWhiteSpace(clientId) ||
                string.IsNullOrWhiteSpace(clientSecret))
            {
                throw new InvalidOperationException(
                    "Missing Graph configuration. Expected Graph:TenantId, Graph:ClientId, Graph:ClientSecret in appsettings.json.");
            }

            _app = ConfidentialClientApplicationBuilder
                .Create(clientId)
                .WithClientSecret(clientSecret)
                .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
                .Build();
        }

        public async Task<GraphAccessToken> AcquireTokenAsync()
        {
            var result = await _app
                .AcquireTokenForClient(GraphScopes)
                .ExecuteAsync();

            return new GraphAccessToken(result.AccessToken, result.ExpiresOn);
        }
    }

    public sealed class GraphTokenProvider : IGraphTokenProvider
    {
        private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(5);
        private readonly IGraphAccessTokenSource _source;
        private readonly SemaphoreSlim _refreshLock = new(1, 1);
        private GraphAccessToken? _cachedToken;

        public GraphTokenProvider(IGraphAccessTokenSource source)
        {
            _source = source;
        }

        public async Task<string> GetTokenAsync()
        {
            if (TryGetCachedToken(out var token))
                return token;

            await _refreshLock.WaitAsync();
            try
            {
                if (TryGetCachedToken(out token))
                    return token;

                _cachedToken = await _source.AcquireTokenAsync();
                return _cachedToken.AccessToken;
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        private bool TryGetCachedToken(out string token)
        {
            if (_cachedToken is not null &&
                _cachedToken.ExpiresOn > DateTimeOffset.UtcNow.Add(RefreshSkew))
            {
                token = _cachedToken.AccessToken;
                return true;
            }

            token = string.Empty;
            return false;
        }
    }

    public class GraphMailService
    {
        private readonly IGraphTokenProvider _tokenProvider;
        private readonly IHttpClientFactory _httpClientFactory;

        public GraphMailService(IGraphTokenProvider tokenProvider, IHttpClientFactory httpClientFactory)
        {
            _tokenProvider = tokenProvider;
            _httpClientFactory = httpClientFactory;
        }

        public async Task SendHtmlAsync(string fromUserPrincipalName, string toEmail, string subject, string htmlBody)
        {
            var token = await _tokenProvider.GetTokenAsync();

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
