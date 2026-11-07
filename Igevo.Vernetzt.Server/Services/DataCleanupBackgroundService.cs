using Igevo.Vernetzt.Core.Interfaces.Services;

namespace Igevo.Vernetzt.Server.Services;

/// <summary>
/// Background service that runs daily data cleanup (expired tokens, old audit logs).
/// Executes once per day at a random time to avoid thundering herd.
/// </summary>
public class DataCleanupBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DataCleanupBackgroundService> _logger;
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(24);

    public DataCleanupBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<DataCleanupBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Data cleanup background service started. Running every {Interval}h", CleanupInterval.TotalHours);

        // Initial delay to let the app finish startup (2 minutes)
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var cleanupService = scope.ServiceProvider.GetRequiredService<IDataCleanupService>();

                var result = await cleanupService.RunCleanupAsync(stoppingToken);

                if (result.TotalDeleted > 0)
                {
                    _logger.LogInformation("Data cleanup completed: {TotalDeleted} records deleted", result.TotalDeleted);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during scheduled data cleanup");
            }

            await Task.Delay(CleanupInterval, stoppingToken);
        }

        _logger.LogInformation("Data cleanup background service stopped");
    }
}
