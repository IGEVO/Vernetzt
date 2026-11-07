using System.Diagnostics;
using System.Reflection;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Igevo.Vernetzt.Server.Controllers;

/// <summary>
/// Controller for health check endpoints.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private const int MinimumHealthTokenLength = 32;

    private readonly VernetztDbContext _dbContext;
    private readonly IFileStorageService _fileStorage;
    private readonly IDatabaseBackupService _backupService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<HealthController> _logger;
    private readonly IWebHostEnvironment _environment;

    public HealthController(
        VernetztDbContext dbContext,
        IFileStorageService fileStorage,
        IDatabaseBackupService backupService,
        IConfiguration configuration,
        ILogger<HealthController> logger,
        IWebHostEnvironment environment)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
        _backupService = backupService;
        _configuration = configuration;
        _logger = logger;
        _environment = environment;
    }

    /// <summary>
    /// Simple health check endpoint for container orchestration (no auth required).
    /// </summary>
    /// <returns>Healthy status if app is running.</returns>
    [HttpGet("/health")]
    [AllowAnonymous]
    [DisableHttpMetrics] // container probe every 30 s, would skew request rate and latency
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> SimpleHealth(CancellationToken cancellationToken)
    {
        try
        {
            // Quick database connectivity check
            var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
            if (!canConnect)
            {
                return StatusCode(503, new { status = "unhealthy", reason = "Database connection failed" });
            }

            return Ok(new { status = "healthy" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            return StatusCode(503, new { status = "unhealthy", reason = "Health check failed" });
        }
    }

    /// <summary>
    /// Detailed health check endpoint secured with token.
    /// Returns comprehensive information about the application state.
    /// </summary>
    /// <param name="token">Health check token from X-Health-Token header.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Detailed health information.</returns>
    [HttpGet("details")]
    [AllowAnonymous]
    [EnableRateLimiting("HealthDetails")]
    [ProducesResponseType(typeof(DetailedHealthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> DetailedHealth(
        [FromHeader(Name = "X-Health-Token")] string? token,
        CancellationToken cancellationToken)
    {
        // Validate token
        var configuredToken = _configuration.GetValue<string>("Health:Token");
        // A committed placeholder (e.g. "CHANGE-THIS-...") or a short value counts as not configured
        if (string.IsNullOrWhiteSpace(configuredToken) ||
            configuredToken.StartsWith("CHANGE-THIS", StringComparison.OrdinalIgnoreCase) ||
            configuredToken.Length < MinimumHealthTokenLength)
        {
            _logger.LogWarning("Health token not configured. Access denied.");
            return Unauthorized(new { error = "Health endpoint not configured" });
        }

        if (string.IsNullOrWhiteSpace(token) || !string.Equals(token, configuredToken, StringComparison.Ordinal))
        {
            _logger.LogWarning("Invalid health token provided");
            return Unauthorized(new { error = "Invalid token" });
        }

        var response = new DetailedHealthResponse
        {
            Timestamp = DateTimeOffset.UtcNow,
            Environment = _environment.EnvironmentName
        };

        // Get version information
        var assembly = Assembly.GetExecutingAssembly();
        var assemblyInfo = assembly.GetName();
        response.Version = new VersionInfo
        {
            AssemblyVersion = assemblyInfo.Version?.ToString() ?? "unknown",
            InformationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
            GitHash = System.Environment.GetEnvironmentVariable("VERNETZT_GIT_HASH") ?? "local"
        };

        // Get runtime information
        response.Runtime = new RuntimeInfo
        {
            DotNetVersion = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            OsDescription = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            ProcessArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString()
        };

        // Get process information
        var process = Process.GetCurrentProcess();
        response.Process = new ProcessInfo
        {
            StartTime = process.StartTime.ToUniversalTime(),
            Uptime = DateTimeOffset.UtcNow - process.StartTime.ToUniversalTime(),
            WorkingSetMb = Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 2),
            PrivateMemoryMb = Math.Round(process.PrivateMemorySize64 / 1024.0 / 1024.0, 2),
            ThreadCount = process.Threads.Count,
            HandleCount = process.HandleCount
        };

        // Check database health
        response.Checks.Add(await CheckDatabaseAsync(cancellationToken));

        // Check storage health
        response.Checks.Add(await CheckStorageAsync(cancellationToken));

        // Check backup health
        response.Checks.Add(CheckBackups());

        // Determine overall status
        response.Status = response.Checks.All(c => c.Status == "healthy") ? "healthy" :
                          response.Checks.Any(c => c.Status == "unhealthy") ? "unhealthy" : "degraded";

        var statusCode = response.Status == "healthy" ? 200 : 503;
        return StatusCode(statusCode, response);
    }

    private async Task<HealthCheck> CheckDatabaseAsync(CancellationToken cancellationToken)
    {
        var check = new HealthCheck
        {
            Name = "database",
            Description = "SQLite database"
        };

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
            stopwatch.Stop();
            check.DurationMs = stopwatch.ElapsedMilliseconds;

            if (canConnect)
            {
                check.Status = "healthy";

                // Get additional database info
                var pendingMigrations = await _dbContext.Database.GetPendingMigrationsAsync(cancellationToken);
                var appliedMigrations = await _dbContext.Database.GetAppliedMigrationsAsync(cancellationToken);

                check.Details = new Dictionary<string, object>
                {
                    { "appliedMigrations", appliedMigrations.Count() },
                    { "pendingMigrations", pendingMigrations.Count() },
                    { "provider", _dbContext.Database.ProviderName ?? "unknown" }
                };

                if (pendingMigrations.Any())
                {
                    check.Status = "degraded";
                    check.Details["pendingMigrationNames"] = pendingMigrations.ToList();
                }
            }
            else
            {
                check.Status = "unhealthy";
                check.Error = "Cannot connect to database";
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            check.DurationMs = stopwatch.ElapsedMilliseconds;
            check.Status = "unhealthy";
            check.Error = ex.Message;
            _logger.LogError(ex, "Database health check failed");
        }

        return check;
    }

    private HealthCheck CheckBackups()
    {
        var check = new HealthCheck
        {
            Name = "backups",
            Description = "Database backups"
        };

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var status = _backupService.GetStatus();
            var latest = status.Backups.FirstOrDefault();
            check.Status = "healthy";
            check.Details = new Dictionary<string, object>
            {
                { "scheduleEnabled", status.ScheduleEnabled },
                { "count", status.Backups.Count }
            };

            if (latest != null)
            {
                check.Details["latestUtc"] = latest.CreatedUtc;
            }

            if (status.NextRunUtc != null)
            {
                check.Details["nextRunUtc"] = status.NextRunUtc;
            }

            // A missing backup is only a problem once the first scheduled run is due.
            if (status.ScheduleEnabled && latest != null && latest.CreatedUtc < DateTime.UtcNow.AddHours(-48))
            {
                check.Status = "degraded";
                check.Error = "Latest backup is older than 48 hours";
            }

            if (status.LastFailureUtc != null && (latest == null || status.LastFailureUtc > latest.CreatedUtc))
            {
                check.Status = "degraded";
                check.Error = $"Last backup attempt failed: {status.LastError}";
            }
        }
        catch (Exception ex)
        {
            check.Status = "unhealthy";
            check.Error = ex.Message;
            _logger.LogError(ex, "Backup health check failed");
        }

        stopwatch.Stop();
        check.DurationMs = stopwatch.ElapsedMilliseconds;
        return check;
    }

    private async Task<HealthCheck> CheckStorageAsync(CancellationToken cancellationToken)
    {
        var check = new HealthCheck
        {
            Name = "storage",
            Description = "File storage accessibility"
        };

        var stopwatch = Stopwatch.StartNew();
        try
        {
            // Write a test file (the storage assigns a unique name and returns its path)
            using var testStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("health"));
            var testPath = await _fileStorage.UploadFileAsync(testStream, ".healthcheck", "text/plain", "avatars", cancellationToken);

            // Verify it exists
            var exists = await _fileStorage.FileExistsAsync(testPath, cancellationToken);

            // Delete test file
            await _fileStorage.DeleteFileAsync(testPath, cancellationToken);

            stopwatch.Stop();
            check.DurationMs = stopwatch.ElapsedMilliseconds;
            check.Status = exists ? "healthy" : "unhealthy";
            check.Details = new Dictionary<string, object>
            {
                { "rootPath", _configuration.GetValue<string>("Storage:RootPath") ?? "not configured" }
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            check.DurationMs = stopwatch.ElapsedMilliseconds;
            check.Status = "unhealthy";
            check.Error = ex.Message;
            _logger.LogError(ex, "Storage health check failed");
        }

        return check;
    }
}

/// <summary>
/// Detailed health response model.
/// </summary>
public class DetailedHealthResponse
{
    /// <summary>
    /// Overall health status: healthy, degraded, or unhealthy.
    /// </summary>
    public string Status { get; set; } = "unknown";

    /// <summary>
    /// UTC timestamp when health check was performed.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Environment name (Development, Production, etc.).
    /// </summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>
    /// Version information.
    /// </summary>
    public VersionInfo Version { get; set; } = new();

    /// <summary>
    /// Runtime information.
    /// </summary>
    public RuntimeInfo Runtime { get; set; } = new();

    /// <summary>
    /// Process information.
    /// </summary>
    public ProcessInfo Process { get; set; } = new();

    /// <summary>
    /// Individual health checks.
    /// </summary>
    public List<HealthCheck> Checks { get; set; } = [];
}

/// <summary>
/// Version information.
/// </summary>
public class VersionInfo
{
    public string AssemblyVersion { get; set; } = string.Empty;
    public string InformationalVersion { get; set; } = string.Empty;
    public string GitHash { get; set; } = string.Empty;
}

/// <summary>
/// Runtime information.
/// </summary>
public class RuntimeInfo
{
    public string DotNetVersion { get; set; } = string.Empty;
    public string OsDescription { get; set; } = string.Empty;
    public string ProcessArchitecture { get; set; } = string.Empty;
}

/// <summary>
/// Process information.
/// </summary>
public class ProcessInfo
{
    public DateTimeOffset StartTime { get; set; }
    public TimeSpan Uptime { get; set; }
    public double WorkingSetMb { get; set; }
    public double PrivateMemoryMb { get; set; }
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
}

/// <summary>
/// Individual health check result.
/// </summary>
public class HealthCheck
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = "unknown";
    public long DurationMs { get; set; }
    public string? Error { get; set; }
    public Dictionary<string, object>? Details { get; set; }
}
