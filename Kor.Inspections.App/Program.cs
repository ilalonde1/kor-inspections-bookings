using Kor.Inspections.App.Data;
using Kor.Inspections.App.Options;
using Kor.Inspections.App.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using System.Text.Json;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext();
});

// --------------------
// Authentication (Azure / Entra ID)
// --------------------

builder.Services
    .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));
builder.Services
    .AddAuthentication()
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, HealthProbeAuthenticationHandler>(
        HealthProbeAuthenticationHandler.SchemeName, _ => { });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("HealthzAccess", policy =>
    {
        // /healthz is for monitoring probes only. OIDC's challenge writes a
        // full 302 redirect and starts the response, which prevents any
        // later scheme from sending 401. Restrict to HealthProbe so probes
        // get a clean 401 on bad/missing keys instead of an HTML login page.
        policy.AuthenticationSchemes.Add(HealthProbeAuthenticationHandler.SchemeName);
        policy.RequireAuthenticatedUser();
    });
});
builder.Services.AddMemoryCache();

builder.Services.AddScoped<ProjectProfileService>();
builder.Services.AddScoped<ProjectBootstrapVerificationService>();


// --------------------
// Razor Pages (ADMIN PROTECTION GOES HERE)
// --------------------

builder.Services.AddRazorPages(options =>
{
    // Protect ONLY the Admin folder
    options.Conventions.AuthorizeFolder("/Admin");
})
.AddMicrosoftIdentityUI();

// --------------------
// Database
// --------------------

builder.Services.AddDbContext<InspectionsContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Sql")));
builder.Services.AddHealthChecks()
    .AddSqlServer(
        builder.Configuration.GetConnectionString("Sql")!,
        healthQuery: "SELECT 1;",
        name: "sql-server");

// --------------------
// Configuration options
// --------------------

builder.Services.Configure<InspectionRulesOptions>(
    builder.Configuration.GetSection("InspectionRules"));

builder.Services.Configure<NotificationOptions>(
    builder.Configuration.GetSection("Notification"));

builder.Services.Configure<AppOptions>(
    builder.Configuration.GetSection("App"));

builder.Services.Configure<SupportOptions>(
    builder.Configuration.GetSection("Support"));

builder.Services.Configure<DeltekProjectOptions>(
    builder.Configuration.GetSection("Deltek"));

// --------------------
// HTTP + core services
// --------------------

builder.Services.AddHttpClient();

builder.Services.AddSingleton<IGraphAccessTokenSource, MsalGraphAccessTokenSource>();
builder.Services.AddSingleton<IGraphTokenProvider, GraphTokenProvider>();
builder.Services.AddScoped<GraphMailService>();
builder.Services.AddScoped<TimeRuleService>();
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<DeltekProjectService>();

// --------------------
// Rate limiting (anti-abuse)
// --------------------

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        // Default rate-limit responses may be empty/plaintext which causes client-side `response.json()` to throw.
        context.HttpContext.Response.ContentType = "application/json";
        var payload = JsonSerializer.Serialize(new
        {
            error = "Too many requests. Please wait a moment and try again."
        });
        await context.HttpContext.Response.WriteAsync(payload, token);
    };

    options.AddPolicy("booking", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetRateLimitPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(30),
                QueueLimit = 0
            }));

    options.AddPolicy("verification", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetRateLimitPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0
            }));

    options.AddPolicy("contactMutation", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0
            }));
});

var app = builder.Build();

ValidateInspectionRulesConfiguration(
    app.Configuration,
    app.Logger,
    strict: app.Environment.IsProduction());
DeltekConfigurationValidator.Validate(
    app.Configuration,
    app.Logger,
    strict: app.Environment.IsProduction());

if (app.Environment.IsProduction())
{
    ValidateRequiredSecret(builder.Configuration, "ConnectionStrings:Sql");
    ValidateRequiredSecret(builder.Configuration, "Graph:ClientSecret");
    ValidateRequiredSecret(builder.Configuration, "AzureAd:ClientSecret");
    ValidateRequiredSecret(builder.Configuration, "Deltek:OdbcDsn");
    ValidateRequiredSecret(builder.Configuration, "Health:ProbeKey");
    ValidateRequiredConfiguration(builder.Configuration, "Notification:FromMailbox");
    ValidateRequiredConfiguration(builder.Configuration, "App:PublicBaseUrl");
}

// --------------------
// Middleware pipeline
// --------------------

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseSerilogRequestLogging();

// Auth must come before authorization
app.UseAuthentication();
app.UseAuthorization();

// Enable rate limiting
app.UseRateLimiter();

// --------------------
// Routing
// --------------------

app.MapRazorPages();
app.MapHealthChecks("/healthz")
    .RequireAuthorization(new Microsoft.AspNetCore.Authorization.AuthorizeAttribute
    {
        Policy = "HealthzAccess",
        AuthenticationSchemes = HealthProbeAuthenticationHandler.SchemeName
    });

app.Run();

static void ValidateRequiredSecret(IConfiguration config, string key)
{
    var value = config[key];
    if (string.IsNullOrWhiteSpace(value) || value.Contains("__SET_", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            $"Missing required production secret configuration: '{key}'. Configure it via environment variables or secure secret store.");
    }
}

static void ValidateRequiredConfiguration(IConfiguration config, string key)
{
    var value = config[key];
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException($"Missing required configuration: '{key}'.");
    }
}

static string GetRateLimitPartitionKey(HttpContext httpContext)
{
    if (httpContext.User?.Identity?.IsAuthenticated == true)
    {
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContext.User.FindFirstValue("sub")
            ?? httpContext.User.Identity?.Name;

        if (!string.IsNullOrWhiteSpace(userId))
            return $"user:{userId}";
    }

    var ip = httpContext.Connection.RemoteIpAddress?.ToString();
    return string.IsNullOrWhiteSpace(ip) ? "ip:unknown" : $"ip:{ip}";
}

static void ValidateInspectionRulesConfiguration(IConfiguration config, Microsoft.Extensions.Logging.ILogger logger, bool strict)
{
    var section = config.GetSection("InspectionRules");
    var options = section.Get<InspectionRulesOptions>() ?? new InspectionRulesOptions();

    var errors = new List<string>();

    if (string.IsNullOrWhiteSpace(options.TimeZoneId))
    {
        errors.Add("InspectionRules:TimeZoneId is required.");
    }
    else
    {
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException || ex is InvalidTimeZoneException)
        {
            errors.Add($"InspectionRules:TimeZoneId '{options.TimeZoneId}' is invalid on this host.");
        }
    }

    if (!TimeOnly.TryParseExact(options.WorkStart, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
    {
        errors.Add($"InspectionRules:WorkStart '{options.WorkStart}' is invalid. Expected format HH:mm.");
    }

    if (!TimeOnly.TryParseExact(options.WorkEnd, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
    {
        errors.Add($"InspectionRules:WorkEnd '{options.WorkEnd}' is invalid. Expected format HH:mm.");
    }

    if (errors.Count == 0)
        return;

    var message = "InspectionRules configuration is invalid: " + string.Join(" ", errors);

    if (strict)
        throw new InvalidOperationException(message);

    logger.LogWarning("{Message} Development mode will continue to run.", message);
}

public partial class Program { }

public static class DeltekConfigurationValidator
{
    public static void Validate(IConfiguration config, Microsoft.Extensions.Logging.ILogger logger, bool strict)
    {
        var section = config.GetSection("Deltek");
        var options = section.Get<DeltekProjectOptions>() ?? new DeltekProjectOptions();

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Sql_ProjectByNumber))
        {
            errors.Add("Deltek:Sql_ProjectByNumber is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Sql_ProjectSearchByPrefix))
        {
            errors.Add("Deltek:Sql_ProjectSearchByPrefix is required.");
        }

        if (errors.Count == 0)
            return;

        var message = "Deltek configuration is invalid: " + string.Join(" ", errors);

        if (strict)
            throw new InvalidOperationException(message);

        logger.LogWarning("{Message} Development mode will continue to run.", message);
    }
}
