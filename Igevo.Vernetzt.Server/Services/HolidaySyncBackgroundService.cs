using Igevo.Vernetzt.Core.Interfaces.Services;

namespace Igevo.Vernetzt.Server.Services;

/// <summary>
/// Synchronizes the external holiday subscriptions once a day.
/// </summary>
public class HolidaySyncBackgroundService(IServiceProvider serviceProvider, ILogger<HolidaySyncBackgroundService> logger)
    : BackgroundService
{
    private static readonly TimeSpan SyncInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(InitialDelay, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = serviceProvider.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<IHolidayService>().SyncAllAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Error during scheduled holiday synchronization");
                }

                await Task.Delay(SyncInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown
        }
    }
}
