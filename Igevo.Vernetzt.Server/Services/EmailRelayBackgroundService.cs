using Igevo.Vernetzt.Core.Interfaces.Services;

namespace Igevo.Vernetzt.Server.Services;

/// <summary>
/// Background service that periodically polls the monitored mailbox
/// for incoming relay emails and processes them.
/// </summary>
public class EmailRelayBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailRelayBackgroundService> _logger;
    private readonly int _pollingIntervalSeconds;
    private readonly bool _isEnabled;
    private int _cleanupCounter;

    public EmailRelayBackgroundService(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<EmailRelayBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
        _pollingIntervalSeconds = int.Parse(configuration["EmailRelay:PollingIntervalSeconds"] ?? "60");
        _isEnabled = bool.Parse(configuration["EmailRelay:Enabled"] ?? "false");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_isEnabled)
        {
            _logger.LogInformation("Email relay background service is disabled via configuration");
            return;
        }

        _logger.LogInformation(
            "Email relay background service started. Polling every {Interval}s",
            _pollingIntervalSeconds);

        // Initial delay to let the app finish startup
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogDebug("Crawling relay mailbox for incoming emails...");

                using var scope = _serviceProvider.CreateScope();
                var relayService = scope.ServiceProvider.GetRequiredService<IEmailRelayService>();

                await relayService.ProcessIncomingEmailsAsync(stoppingToken);

                // Cleanup expired tokens every ~10 polling cycles
                _cleanupCounter++;
                if (_cleanupCounter >= 10)
                {
                    _cleanupCounter = 0;
                    await relayService.CleanupExpiredTokensAsync(stoppingToken);
                    await relayService.CleanupMailboxAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in email relay background processing. Will retry next cycle.");
            }

            await Task.Delay(TimeSpan.FromSeconds(_pollingIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("Email relay background service stopped");
    }
}
