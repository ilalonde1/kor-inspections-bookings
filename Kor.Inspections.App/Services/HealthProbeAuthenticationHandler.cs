using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Inspections.App.Services
{
    public sealed class HealthProbeAuthenticationHandler
        : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "HealthProbe";
        public const string HeaderName = "X-Health-Probe-Key";
        private const string ConfigKey = "Health:ProbeKey";

        private readonly IConfiguration _config;

        public HealthProbeAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IConfiguration config)
            : base(options, logger, encoder)
        {
            _config = config;
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(HeaderName, out var providedKeyValues))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var providedKey = providedKeyValues.ToString();
            if (string.IsNullOrEmpty(providedKey))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var configured = _config[ConfigKey];
            if (string.IsNullOrEmpty(configured))
            {
                Logger.LogWarning(
                    "Health probe rejected: {ConfigKey} is not configured.", ConfigKey);
                return Task.FromResult(AuthenticateResult.Fail("Health probe key not configured."));
            }

            // Hash both sides so FixedTimeEquals always sees equal-length inputs
            // and the comparison itself is constant-time.
            var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(providedKey));
            var configuredHash = SHA256.HashData(Encoding.UTF8.GetBytes(configured));

            if (!CryptographicOperations.FixedTimeEquals(providedHash, configuredHash))
            {
                return Task.FromResult(AuthenticateResult.Fail("Invalid health probe key."));
            }

            var identity = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, "health-probe") },
                SchemeName);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            // Probes are headless tools — return 401 instead of redirecting to a login page.
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
    }
}
