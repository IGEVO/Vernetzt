using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HostFiltering;
using Igevo.Vernetzt.Server.Controllers;
using Igevo.Vernetzt.Server.Configuration;
using Igevo.Vernetzt.Server.Identity;
using Igevo.Vernetzt.Server.Services;
using Igevo.Vernetzt.Server.Security;
using Igevo.Vernetzt.Core.Configuration;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Data;
using Igevo.Vernetzt.Infrastructure.Identity;
using Igevo.Vernetzt.Infrastructure.Observability;
using Igevo.Vernetzt.Infrastructure.Repositories;
using Igevo.Vernetzt.Infrastructure.Services;
using Igevo.Vernetzt.Infrastructure.Storage;
using Igevo.Vernetzt.Infrastructure.Validators;
using FluentValidation;
using Ganss.Xss;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using OpenTelemetry.Metrics;
using Swashbuckle.AspNetCore.Swagger;
using BclIPNetwork = System.Net.IPNetwork;

var builder = WebApplication.CreateBuilder(args);

// Add Aspire service defaults (OpenTelemetry, health checks, service discovery)
builder.AddServiceDefaults();

// Business metrics (meter "Igevo.Vernetzt"), exported with the other OpenTelemetry metrics
builder.Services.AddSingleton<VernetztMetrics>();
builder.Services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddMeter(VernetztMetrics.MeterName));

// Add Controllers
builder.Services.AddControllers();

// Rich text is untrusted input. Keep this allow-list aligned with the Vue editor toolbar.
builder.Services.AddSingleton<IHtmlSanitizer>(_ => EmailHtmlSanitizerFactory.Create());

// Add API Explorer for Swagger
builder.Services.AddEndpointsApiExplorer();

// The public contract is generated in every environment; the interactive UI remains development-only.
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
    {
        Title = "Igevo.Vernetzt API",
        Version = "v1",
        Description = "REST API for connecting clubs and organizations"
    });
    options.SwaggerDoc("public-v1", new Microsoft.OpenApi.OpenApiInfo
    {
        Title = "Vernetzt Public API",
        Version = "v1",
        Description = "Read-only public API for clubs, departments and events"
    });
    options.DocInclusionPredicate((documentName, description) =>
        documentName == "public-v1"
            ? description.GroupName == "public-v1"
            : description.GroupName != "public-v1");
});

// Configure App Settings
builder.Services.Configure<AppSettings>(
    builder.Configuration.GetSection(AppSettings.SectionName));
builder.Services.Configure<PasskeyOptions>(
    builder.Configuration.GetSection(PasskeyOptions.SectionName));

// The container health check (Dockerfile HEALTHCHECK) calls http://localhost:8080/health. Loopback host
// names stay allowed, so that AllowedHosts can list only the public domain without the container becoming unhealthy.
builder.Services.PostConfigure<HostFilteringOptions>(options =>
{
    if (options.AllowedHosts.Count == 0 || options.AllowedHosts.Contains("*"))
    {
        return;
    }

    options.AllowedHosts = options.AllowedHosts
        .Concat(["localhost", "127.0.0.1"])
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
});

// Configure reverse-proxy settings (trusted forwarded headers)
var reverseProxySettings = BuildReverseProxySettings(builder.Configuration);
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = reverseProxySettings.ForwardLimit;

    // Restrictive default: trust only explicitly configured proxies/networks.
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Clear();

    foreach (var knownProxy in reverseProxySettings.KnownProxies)
    {
        options.KnownProxies.Add(knownProxy);
    }

    foreach (var knownNetwork in reverseProxySettings.KnownNetworks)
    {
        options.KnownIPNetworks.Add(knownNetwork);
    }
});

// Configure Entity Framework Core (SQLite).
// Database:UseTemporaryDatabase gives every application start its own empty database file in the temp
// directory (automated tests); it is deleted again when the application stops.
string connectionString;
string? temporaryDatabasePath = null;
if (builder.Configuration.GetValue<bool>("Database:UseTemporaryDatabase"))
{
    // Test servers killed without a graceful shutdown (e.g. by Playwright) cannot delete their files.
    SqliteDatabase.DeleteStaleTemporaryFiles(Path.GetTempPath(), "vernetzt-test-", DateTime.UtcNow.AddHours(-1));
    temporaryDatabasePath = Path.Combine(Path.GetTempPath(), $"vernetzt-test-{Guid.NewGuid():N}.db");
    connectionString = $"Data Source={temporaryDatabasePath}";
}
else
{
    connectionString = SqliteDatabase.PrepareConnectionString(
        builder.Configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not configured"),
        builder.Environment.ContentRootPath);
}

builder.Services.AddDbContext<VernetztDbContext>(options =>
{
    options.UseSqlite(connectionString);

    if (builder.Environment.IsDevelopment() || temporaryDatabasePath != null)
    {
        options.EnableDetailedErrors();
        options.EnableSensitiveDataLogging();
    }
});

// Database backups (VACUUM INTO): daily job, manual backups by admins, backup before migrations
builder.Services.Configure<BackupOptions>(builder.Configuration.GetSection(BackupOptions.SectionName));
var backupOptions = builder.Configuration.GetSection(BackupOptions.SectionName).Get<BackupOptions>() ?? new BackupOptions();
var backupDirectory = !string.IsNullOrWhiteSpace(backupOptions.Path)
    ? Path.GetFullPath(backupOptions.Path, builder.Environment.ContentRootPath)
    : temporaryDatabasePath != null
        ? temporaryDatabasePath + "-backups"
        : Path.Combine(builder.Environment.ContentRootPath, "App_Data", "backups");
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new DatabaseBackupSettings(connectionString, backupDirectory, backupOptions));
builder.Services.AddSingleton<IDatabaseBackupService, DatabaseBackupService>();
if (backupOptions.Enabled)
{
    builder.Services.AddHostedService<DatabaseBackupBackgroundService>();
}

// Add HttpContextAccessor for services that need access to HTTP context
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
// Key ring in the database: auth cookies, login links and protected tokens must survive restarts/redeployments.
// Never change the application name: doing so invalidates all sessions, login links and calendar feed URLs.
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("Igevo.Vernetzt")
    .PersistKeysToDbContext<VernetztDbContext>();

// Encrypt the keys at rest with a certificate kept outside the database, so a database dump or backup
// alone cannot be used to forge sessions or login links (see docs/HOSTING.md).
var dataProtectionCertificatePath = builder.Configuration["DataProtection:CertificatePath"];
if (!string.IsNullOrWhiteSpace(dataProtectionCertificatePath))
{
    var dataProtectionCertificate = X509CertificateLoader.LoadPkcs12FromFile(
        dataProtectionCertificatePath,
        builder.Configuration["DataProtection:CertificatePassword"]);
    dataProtection.ProtectKeysWithCertificate(dataProtectionCertificate);
}
else if (builder.Environment.IsProduction())
{
    throw new InvalidOperationException(
        "DataProtection:CertificatePath is required in production. See docs/HOSTING.md.");
}

// Register services
builder.Services.AddScoped<DatabaseInitializationService>();
builder.Services.AddScoped<Igevo.Vernetzt.Core.Interfaces.Services.IAuthService, AuthService>();
builder.Services.AddScoped<Igevo.Vernetzt.Core.Interfaces.Services.IAuthorizationService, AuthorizationService>();
builder.Services.AddScoped<IClubService, ClubService>();
builder.Services.AddScoped<IDepartmentService, DepartmentService>();
builder.Services.AddScoped<IClubTypeService, ClubTypeService>();
builder.Services.AddScoped<ILocationService, LocationService>();
builder.Services.AddScoped<IContactPositionService, ContactPositionService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IAccountEmailChangeService, AccountEmailChangeService>();
builder.Services.AddScoped<IContactAssignmentConsentService, ContactAssignmentConsentService>();
builder.Services.AddScoped<IEventService, EventService>();
builder.Services.AddScoped<IPublicDirectoryService, PublicDirectoryService>();
builder.Services.AddScoped<ICalendarFeedService, CalendarFeedService>();
builder.Services.AddScoped<ICardDavService, Igevo.Vernetzt.Infrastructure.Services.CardDav.CardDavService>();
builder.Services.AddScoped<IRecipientResolutionService, RecipientResolutionService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IPendingItemsService, PendingItemsService>();
builder.Services.AddScoped<IAppSettingsService, AppSettingsService>();
builder.Services.AddScoped<Igevo.Vernetzt.Server.Services.PortalSettingsService>();
builder.Services.AddInfrastructureEmailSender(); // registers ISmtpSender + IEmailService
builder.Services.AddScoped<IDataCleanupService, DataCleanupService>();
builder.Services.AddScoped<DtoMappingService>();

// Data cleanup background service (daily)
builder.Services.AddHostedService<Igevo.Vernetzt.Server.Services.DataCleanupBackgroundService>();

// Central holiday calendar: external iCal subscriptions are synchronized daily.
// Holidays:AllowPrivateNetworks only for local development against feeds in the internal network.
var allowPrivateHolidayFeeds = builder.Configuration.GetValue<bool>("Holidays:AllowPrivateNetworks");
builder.Services.AddHttpClient<IHolidayFeedFetcher, HolidayFeedFetcher>(client =>
    {
        client.Timeout = HolidayFeedFetcher.Timeout;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Vernetzt-HolidayCalendar/1.0");
    })
    .ConfigurePrimaryHttpMessageHandler(() => HolidayFeedFetcher.CreateHandler(allowPrivateHolidayFeeds));
builder.Services.AddScoped<IHolidayService, HolidayService>();
builder.Services.AddHostedService<Igevo.Vernetzt.Server.Services.HolidaySyncBackgroundService>();

// Email relay services
builder.Services.AddScoped<IEmailRelayService, EmailRelayService>();
builder.Services.AddScoped<IEmailRelayTokenRepository, EmailRelayTokenRepository>();

var emailRelayEnabled = builder.Configuration.GetValue<bool>("EmailRelay:Enabled");
if (emailRelayEnabled)
{
    var monitorMode = builder.Configuration["EmailRelay:MonitorMode"] ?? "Imap";
    if (monitorMode.Equals("MailDevApi", StringComparison.OrdinalIgnoreCase))
    {
        builder.Services.AddHttpClient<IMailboxMonitorService, MailDevMailboxMonitorService>();
    }
    else if (monitorMode.Equals("Pop3", StringComparison.OrdinalIgnoreCase))
    {
        builder.Services.AddScoped<IMailboxMonitorService, Pop3MailboxMonitorService>();
    }
    else
    {
        builder.Services.AddScoped<IMailboxMonitorService, ImapMailboxMonitorService>();
    }

    builder.Services.AddHostedService<Igevo.Vernetzt.Server.Services.EmailRelayBackgroundService>();
}
else
{
    // Register a no-op IMAP implementation so DI doesn't fail for controller
    builder.Services.AddScoped<IMailboxMonitorService, ImapMailboxMonitorService>();
}

// File storage and image processing services
var configuredStoragePath = builder.Configuration.GetValue<string>("Storage:RootPath");
var storageRootPath = !string.IsNullOrEmpty(configuredStoragePath)
    ? Path.IsPathRooted(configuredStoragePath)
        ? configuredStoragePath
        : Path.Combine(builder.Environment.ContentRootPath, configuredStoragePath)
    : temporaryDatabasePath != null
        ? temporaryDatabasePath + "-storage"
        : Path.Combine(builder.Environment.ContentRootPath, "App_Data", "storage");

builder.Services.AddSingleton<IFileStorageService>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<LocalFileStorageService>>();
    return new LocalFileStorageService(storageRootPath, logger);
});
builder.Services.AddSingleton<IImageProcessingService, ImageProcessingService>();
builder.Services.AddScoped<IAvatarService, AvatarService>();

// Register repositories
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IClubRepository, ClubRepository>();
builder.Services.AddScoped<IClubTypeRepository, ClubTypeRepository>();
builder.Services.AddScoped<IContactPositionRepository, ContactPositionRepository>();
builder.Services.AddScoped<IDepartmentRepository, DepartmentRepository>();
builder.Services.AddScoped<IEventRepository, EventRepository>();
builder.Services.AddScoped<ICalendarFeedTokenRepository, CalendarFeedTokenRepository>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
builder.Services.AddScoped<IInvitationTokenRepository, InvitationTokenRepository>();

// Register FluentValidation validators from Infrastructure assembly
builder.Services.AddValidatorsFromAssemblyContaining<CreateClubDtoValidator>();

// Authentication: ASP.NET Core Identity (cookie session, passkeys, login-link tokens).
// The Identity account (AspNetUsers) accompanies each domain user 1:1; roles stay on the domain user.
builder.Services
    .AddIdentityCore<AppIdentityUser>(options =>
    {
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3; // includes passkeys
        options.User.RequireUniqueEmail = false; // uniqueness is enforced on the domain user
        options.SignIn.RequireConfirmedAccount = false;
        options.SignIn.RequireConfirmedEmail = false;
        options.Lockout.AllowedForNewUsers = false; // no passwords, nothing to brute-force here
    })
    .AddEntityFrameworkStores<VernetztDbContext>()
    .AddSignInManager()
    .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>()
    .AddTokenProvider<LoginLinkTokenProvider>(LoginLinkTokens.ProviderName);

builder.Services.Configure<LoginLinkTokenProviderOptions>(options =>
    options.TokenLifespan = TimeSpan.FromMinutes(LoginLinkTokens.GetExpirationMinutes(builder.Configuration)));

var passkeySettings = builder.Configuration.GetSection(PasskeyOptions.SectionName).Get<PasskeyOptions>() ?? new PasskeyOptions();
var allowedPasskeyOrigins = new HashSet<string>(
    passkeySettings.AllowedOrigins.Select(o => o.TrimEnd('/')),
    StringComparer.OrdinalIgnoreCase);
builder.Services.Configure<IdentityPasskeyOptions>(options =>
{
    if (!string.IsNullOrWhiteSpace(passkeySettings.RelyingPartyId))
    {
        options.ServerDomain = passkeySettings.RelyingPartyId;
    }

    options.AuthenticatorTimeout = TimeSpan.FromSeconds(passkeySettings.ChallengeTimeoutSeconds);

    // In development the browser talks to the Vite dev server, so the WebAuthn origin differs
    // from the API host. Accept exactly the configured origins.
    if (allowedPasskeyOrigins.Count > 0)
    {
        options.ValidateOrigin = context =>
            ValueTask.FromResult(!context.CrossOrigin && allowedPasskeyOrigins.Contains(context.Origin.TrimEnd('/')));
    }
});

builder.Services
    .AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();

// Read-only CardDAV address book: HTTP Basic with a personal app password, only on /carddav.
builder.Services
    .AddAuthentication()
    .AddScheme<AuthenticationSchemeOptions, CardDavBasicAuthenticationHandler>(
        CardDavBasicAuthenticationHandler.SchemeName, _ => { });

var sessionDays = builder.Configuration.GetValue<int?>("Auth:Session:ExpirationDays") ?? 14;
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = AuthCookies.SessionCookieName;
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromDays(sessionDays);
    options.SlidingExpiration = true;

    // This is an API + SPA: never redirect, answer with status codes.
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

// Re-validate the session (security stamp, user still exists, fresh roles) every few minutes.
// "Log out on all devices" rotates the security stamp.
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.FromMinutes(
        builder.Configuration.GetValue<int?>("Auth:Session:ValidationIntervalMinutes") ?? 5);
    // Keep "passkey" vs. "login-link" across principal refreshes (needed by the passkey policy).
    options.OnRefreshingPrincipal = PasskeyPolicy.KeepAuthenticationMethod;
});

// Roles that may only be used from a passkey session (default: SystemAdmin and Manager).
builder.Services.Configure<PasskeyPolicyOptions>(builder.Configuration.GetSection(PasskeyPolicyOptions.SectionName));
builder.Services.AddScoped<PasskeyPolicy>();

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// Permit limits are configurable (e.g. raised for automated end-to-end tests that run from one IP).
var publicAuthPermitLimit = builder.Configuration.GetValue("RateLimiting:PublicAuth:PermitLimit", 10);
var publicTokenFlowPermitLimit = builder.Configuration.GetValue("RateLimiting:PublicTokenFlow:PermitLimit", 20);
var publicApiPermitLimit = builder.Configuration.GetValue("RateLimiting:PublicApi:PermitLimit", 120);
var cardDavPermitLimit = builder.Configuration.GetValue("RateLimiting:CardDav:PermitLimit", 120);
// Revealing email addresses and phone numbers is meant for people, not crawlers: a handful per minute.
var publicContactRevealPermitLimit = builder.Configuration.GetValue("RateLimiting:PublicContactReveal:PermitLimit", 10);

builder.Services.AddOutputCache(options =>
{
    options.AddPolicy("PublicApi", policy => policy
        .Expire(TimeSpan.FromSeconds(60))
        .SetVaryByQuery("*"));
    options.AddPolicy("PublicMedia", policy => policy
        .Expire(TimeSpan.FromSeconds(60))
        .SetVaryByQuery("*"));
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("PublicAuth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: BuildRateLimitPartitionKey(context, "public-auth"),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = publicAuthPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("PublicTokenFlow", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: BuildRateLimitPartitionKey(context, "public-token-flow"),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = publicTokenFlowPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    // CardDAV clients sync in bursts (PROPFIND + REPORT); generous per IP, failed logins are limited separately.
    options.AddPolicy(CardDavController.RateLimitPolicyName, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: BuildRateLimitPartitionKey(context, "carddav"),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = cardDavPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("PublicApi", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: BuildRateLimitPartitionKey(context, "public-api"),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = publicApiPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy(PublicDirectoryController.ContactRevealRateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: BuildRateLimitPartitionKey(context, "public-contact-reveal"),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = publicContactRevealPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("HealthDetails", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: BuildRateLimitPartitionKey(context, "health-details"),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

var app = builder.Build();

if (temporaryDatabasePath != null)
{
    app.Lifetime.ApplicationStopped.Register(() =>
    {
        SqliteDatabase.DeleteDatabaseFiles(temporaryDatabasePath);
        foreach (var directory in new[] { backupDirectory, storageRootPath })
        {
            if (Directory.Exists(directory) && directory.StartsWith(temporaryDatabasePath, StringComparison.Ordinal))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    });
}

// Initialize database on startup
using (var scope = app.Services.CreateScope())
{
    var dbInitializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializationService>();
    await dbInitializer.InitializeAsync();
}

// Send server startup notification to admin (fire-and-forget: must not block app.Run())
{
    var startupConfig = app.Services.GetRequiredService<IConfiguration>();
    var adminEmail = startupConfig["App:SystemAdminEmail"];

    if (!string.IsNullOrWhiteSpace(adminEmail))
    {
        var environment = app.Environment.EnvironmentName;
        var version = System.Reflection.Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString() ?? "unknown";
        var serverInfo = $@"
Environment: {environment}
Version: {version}
Started: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
SMTP Host: {startupConfig["Email:Smtp:Host"]}
SMTP Port: {startupConfig["Email:Smtp:Port"]}
";
        var capturedAdminEmail = adminEmail;
        var capturedServerInfo = serverInfo;
        var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();

        _ = Task.Run(async () =>
        {
            try
            {
                using var emailScope = app.Services.CreateScope();
                var emailService = emailScope.ServiceProvider.GetRequiredService<IEmailService>();
                await emailService.SendServerStartupNotificationAsync(capturedAdminEmail, capturedServerInfo);
            }
            catch (Exception ex)
            {
                startupLogger.LogWarning(ex, "Failed to send startup notification email");
            }
        });
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Igevo.Vernetzt API v1");
        options.SwaggerEndpoint("/swagger/public-v1/swagger.json", "Vernetzt Public API v1");
        options.RoutePrefix = "swagger";
    });
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

foreach (var parseWarning in reverseProxySettings.ParseWarnings)
{
    app.Logger.LogWarning("Reverse proxy configuration warning: {Warning}", parseWarning);
}

if (reverseProxySettings.Enabled)
{
    app.UseForwardedHeaders();
    app.Logger.LogInformation(
        "Trusted reverse-proxy forwarding enabled (KnownProxies: {KnownProxies}, KnownNetworks: {KnownNetworks}, ForwardLimit: {ForwardLimit})",
        reverseProxySettings.KnownProxies.Count,
        reverseProxySettings.KnownNetworks.Count,
        reverseProxySettings.ForwardLimit);
}

// In development Vite proxies to the HTTP endpoint while an HTTPS endpoint exists for CardDAV clients;
// redirecting there would break the proxy. Production always redirects.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// Browser-facing security policy. Vuetify requires inline style attributes, but scripts stay self-only.
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'; worker-src 'self' blob:; manifest-src 'self'";
        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), publickey-credentials-get=(self), publickey-credentials-create=(self)";
        return Task.CompletedTask;
    });
    await next();
});

// Cookie-authenticated state changes must originate from this application in production.
app.Use(async (context, next) =>
{
    var isUnsafeApiRequest = context.Request.Path.StartsWithSegments("/api")
        && !HttpMethods.IsGet(context.Request.Method)
        && !HttpMethods.IsHead(context.Request.Method)
        && !HttpMethods.IsOptions(context.Request.Method)
        && context.Request.Cookies.ContainsKey(AuthCookies.SessionCookieName);

    if (!app.Environment.IsDevelopment() && isUnsafeApiRequest)
    {
        var origin = context.Request.Headers.Origin.ToString();
        var configuredBaseUrl = app.Configuration["App:BaseUrl"];
        var expectedOrigin = Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var configuredUri)
            ? configuredUri.GetLeftPart(UriPartial.Authority)
            : $"{context.Request.Scheme}://{context.Request.Host}";

        if (!string.Equals(origin, expectedOrigin, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { success = false, message = "Ungültiger Anfrageursprung." });
            return;
        }
    }

    await next();
});

// The anonymous public API serves only the public portal, authenticated by a shared key.
if (!PublicApiAccessMiddleware.IsUsableKey(app.Configuration[PublicApiAccessMiddleware.ConfigurationKey]))
{
    app.Logger.LogWarning(
        "{ConfigurationKey} is not set (at least {MinimumLength} characters). The public API is closed and the public portal cannot load data.",
        PublicApiAccessMiddleware.ConfigurationKey,
        PublicApiAccessMiddleware.MinimumKeyLength);
}
app.UseMiddleware<PublicApiAccessMiddleware>();

app.UseStaticFiles();

app.UseRouting();

app.UseRateLimiter();
app.UseOutputCache();
	
// Add authentication and authorization middleware
app.UseAuthentication();
app.UseMiddleware<PasskeyPolicyMiddleware>();
app.UseAuthorization();

// Map controllers for API endpoints
app.MapControllers();

// Publish only the explicitly separated public contract in production.
app.MapGet("/api/public/v1/openapi.json", (ISwaggerProvider swaggerProvider) =>
    {
        var document = swaggerProvider.GetSwagger("public-v1");
        using var textWriter = new StringWriter();
        var jsonWriter = new OpenApiJsonWriter(textWriter);
        document.SerializeAsV3(jsonWriter);
        return Results.Text(textWriter.ToString(), "application/json; charset=utf-8");
    })
    .AllowAnonymous()
    .RequireRateLimiting("PublicApi")
    .CacheOutput("PublicApi");

// Map Aspire default endpoints (health checks)
app.MapDefaultEndpoints();

// Reserved server paths must never be handled by the SPA fallback.
app.Map("/api/{**path}", () => Results.NotFound()).AllowAnonymous();
app.Map("/health/{**path}", () => Results.NotFound()).AllowAnonymous();
app.Map("/swagger/{**path}", () => Results.NotFound()).AllowAnonymous();
// The user documentation is no longer shipped with each instance; old /help links go to GitHub Pages.
const string userDocsUrl = "https://igevo.github.io/Vernetzt/";
app.MapGet("/help", () => Results.Redirect(userDocsUrl)).AllowAnonymous();
app.MapGet("/help/{**path}", (string? path) =>
    {
        var page = path ?? string.Empty;
        if (page.EndsWith("index.html", StringComparison.OrdinalIgnoreCase))
        {
            page = page[..^"index.html".Length];
        }
        return Results.Redirect(userDocsUrl + Uri.EscapeDataString(page).Replace("%2F", "/", StringComparison.Ordinal));
    })
    .AllowAnonymous();

// Fallback to index.html for client-side routing (must be last)
app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();

static ReverseProxySettings BuildReverseProxySettings(IConfiguration configuration)
{
    var section = configuration.GetSection("ReverseProxy");
    var enabled = section.GetValue<bool>("Enabled");
    var parseWarnings = new List<string>();

    var forwardLimit = section.GetValue<int?>("ForwardLimit") ?? 1;
    if (forwardLimit < 1)
    {
        parseWarnings.Add($"Invalid ReverseProxy:ForwardLimit '{forwardLimit}'. Using default value 1.");
        forwardLimit = 1;
    }

    var knownProxies = new List<IPAddress>();
    var knownProxyEntries = section.GetSection("KnownProxies").Get<string[]>() ?? [];
    foreach (var entry in knownProxyEntries)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            continue;
        }

        if (IPAddress.TryParse(entry.Trim(), out var proxy))
        {
            knownProxies.Add(proxy);
        }
        else
        {
            parseWarnings.Add($"Invalid ReverseProxy:KnownProxies entry '{entry}' ignored.");
        }
    }

    var knownNetworks = new List<BclIPNetwork>();
    var knownNetworkEntries = section.GetSection("KnownNetworks").Get<string[]>() ?? [];
    foreach (var entry in knownNetworkEntries)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            continue;
        }

        if (TryParseIpNetwork(entry.Trim(), out var network))
        {
            knownNetworks.Add(network);
        }
        else
        {
            parseWarnings.Add($"Invalid ReverseProxy:KnownNetworks entry '{entry}' ignored.");
        }
    }

    if (enabled && knownProxies.Count == 0 && knownNetworks.Count == 0)
    {
        parseWarnings.Add(
            "ReverseProxy is enabled but no trusted proxies or networks are configured. " +
            "Forwarded headers from untrusted sources will be ignored.");
    }

    return new ReverseProxySettings(enabled, forwardLimit, knownProxies, knownNetworks, parseWarnings);
}

static bool TryParseIpNetwork(string value, out BclIPNetwork network)
{
    network = default;

    var parts = value.Split('/', 2, StringSplitOptions.TrimEntries);
    if (parts.Length != 2)
    {
        return false;
    }

    if (!IPAddress.TryParse(parts[0], out var address))
    {
        return false;
    }

    if (!int.TryParse(parts[1], out var prefixLength))
    {
        return false;
    }

    var maxPrefixLength = address.AddressFamily switch
    {
        AddressFamily.InterNetwork => 32,
        AddressFamily.InterNetworkV6 => 128,
        _ => -1
    };

    if (maxPrefixLength < 0 || prefixLength < 0 || prefixLength > maxPrefixLength)
    {
        return false;
    }

    network = new BclIPNetwork(address, prefixLength);
    return true;
}

static string BuildRateLimitPartitionKey(HttpContext context, string policyName)
{
    var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    return $"{policyName}:{ip}";
}

internal sealed record ReverseProxySettings(
    bool Enabled,
    int ForwardLimit,
    List<IPAddress> KnownProxies,
    List<BclIPNetwork> KnownNetworks,
    List<string> ParseWarnings);

// Make Program class accessible for integration tests
public partial class Program { }
