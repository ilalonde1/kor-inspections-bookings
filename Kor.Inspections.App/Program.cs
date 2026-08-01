using Kor.Inspections.App.Data;
using Kor.Inspections.App.Options;
using Kor.Inspections.App.Services;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
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

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("HealthzAccess", policy =>
        policy.RequireAuthenticatedUser());
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
// Enforces [EnableRateLimiting] on page handler methods — the built-in
// middleware ignores handler-level attributes (see HandlerRateLimiting.cs).
.AddMvcOptions(options => options.Filters.Add<PageHandlerRateLimitFilter>())
.AddMicrosoftIdentityUI();

// --------------------
// Database
// --------------------

builder.Services.AddDbContext<InspectionsContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Sql")));
builder.Services.AddHealthChecks()
    .AddDbContextCheck<InspectionsContext>();

// --------------------
// Configuration options
// --------------------

builder.Services.AddOptions<InspectionRulesOptions>()
    .Bind(builder.Configuration.GetSection("InspectionRules"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<NotificationOptions>()
    .Bind(builder.Configuration.GetSection("Notification"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<AppOptions>()
    .Bind(builder.Configuration.GetSection("App"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<SupportOptions>()
    .Bind(builder.Configuration.GetSection("Support"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<DeltekProjectOptions>()
    .Bind(builder.Configuration.GetSection("Deltek"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// --------------------
// HTTP + core services
// --------------------

builder.Services.AddHttpClient();
builder.Services.AddHttpClient("GraphMail", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddSingleton<IGraphAccessTokenSource, MsalGraphAccessTokenSource>();
builder.Services.AddSingleton<IGraphTokenProvider, GraphTokenProvider>();
builder.Services.AddScoped<GraphMailService>();
builder.Services.AddScoped<TimeRuleService>();
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<DeltekProjectService>();

// --------------------
// Rate limiting (anti-abuse)
// --------------------

// Policies live in HandlerRateLimiterService and are enforced by
// PageHandlerRateLimitFilter (registered with AddRazorPages above).
builder.Services.AddSingleton<HandlerRateLimiterService>();

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
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        const int sevenDaysSeconds = 7 * 24 * 60 * 60;
        ctx.Context.Response.Headers["Cache-Control"] =
            $"public, max-age={sevenDaysSeconds}";
    }
});

app.UseRouting();
app.UseSerilogRequestLogging();

// Auth must come before authorization
app.UseAuthentication();
app.UseAuthorization();

// --------------------
// Routing
// --------------------

app.MapRazorPages();
app.MapHealthChecks("/healthz")
    .RequireAuthorization("HealthzAccess");

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
