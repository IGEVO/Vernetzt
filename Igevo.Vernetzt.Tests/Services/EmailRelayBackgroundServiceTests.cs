using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Server.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace Igevo.Vernetzt.Tests.Services;

[TestClass]
public class EmailRelayBackgroundServiceTests
{
    private Mock<IEmailRelayService> _relayServiceMock = null!;
    private Mock<ILogger<EmailRelayBackgroundService>> _loggerMock = null!;

    [TestInitialize]
    public void Setup()
    {
        _relayServiceMock = new Mock<IEmailRelayService>();
        _loggerMock = new Mock<ILogger<EmailRelayBackgroundService>>();
    }

    private EmailRelayBackgroundService CreateBackgroundService(Dictionary<string, string?> configData)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddScoped(_ => _relayServiceMock.Object);
        var serviceProvider = serviceCollection.BuildServiceProvider();

        return new EmailRelayBackgroundService(serviceProvider, config, _loggerMock.Object);
    }

    [TestMethod]
    public async Task ExecuteAsync_DisabledConfig_NeverCallsProcessEmails()
    {
        var configData = new Dictionary<string, string?>
        {
            { "EmailRelay:Enabled", "false" },
            { "EmailRelay:PollingIntervalSeconds", "1" }
        };

        var service = CreateBackgroundService(configData);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await service.StartAsync(cts.Token);
        await Task.Delay(1500);
        await service.StopAsync(CancellationToken.None);

        _relayServiceMock.Verify(s => s.ProcessIncomingEmailsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ExecuteAsync_Enabled_CallsProcessIncomingEmails()
    {
        var configData = new Dictionary<string, string?>
        {
            { "EmailRelay:Enabled", "true" },
            { "EmailRelay:PollingIntervalSeconds", "1" }
        };

        var service = CreateBackgroundService(configData);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await service.StartAsync(cts.Token);
        // Wait long enough for initial delay (5s) + at least one poll
        await Task.Delay(7000);
        await service.StopAsync(CancellationToken.None);

        _relayServiceMock.Verify(s => s.ProcessIncomingEmailsAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [TestMethod]
    public async Task ExecuteAsync_ExceptionInProcessing_ContinuesPolling()
    {
        var callCount = 0;
        _relayServiceMock
            .Setup(s => s.ProcessIncomingEmailsAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount == 1)
                    throw new InvalidOperationException("Test error");
                return Task.CompletedTask;
            });

        var configData = new Dictionary<string, string?>
        {
            { "EmailRelay:Enabled", "true" },
            { "EmailRelay:PollingIntervalSeconds", "1" }
        };

        var service = CreateBackgroundService(configData);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await service.StartAsync(cts.Token);
        // Wait for initial delay (5s) + at least 2 poll cycles
        await Task.Delay(9000);
        await service.StopAsync(CancellationToken.None);

        // Should have been called multiple times despite first call throwing
        Assert.IsTrue(callCount >= 2, $"Expected at least 2 calls but got {callCount}");
    }
}
