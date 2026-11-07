using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Services;

namespace Igevo.Vernetzt.Server.Services;

/// <summary>
/// Creates the scheduled database backup once a day at the configured local time (Backup:Time).
/// </summary>
public class DatabaseBackupBackgroundService : BackgroundService
{
    private readonly IDatabaseBackupService _backupService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DatabaseBackupBackgroundService> _logger;

    public DatabaseBackupBackgroundService(
        IDatabaseBackupService backupService,
        TimeProvider timeProvider,
        ILogger<DatabaseBackupBackgroundService> logger)
    {
        _backupService = backupService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var nextRunUtc = _backupService.GetNextScheduledRunUtc(_timeProvider.GetUtcNow());
            _logger.LogInformation("Next scheduled database backup at {NextRunUtc:u}", nextRunUtc);

            try
            {
                var delay = nextRunUtc - _timeProvider.GetUtcNow().UtcDateTime;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, _timeProvider, stoppingToken);
                }

                await _backupService.CreateBackupAsync(BackupTrigger.Scheduled, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Already logged and recorded by the backup service; try again at the next scheduled time.
                _logger.LogDebug(ex, "Scheduled database backup failed");
            }
        }
    }
}
