using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Services;
using Igevo.Vernetzt.Tests.TestInfrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Polly.CircuitBreaker;

namespace Igevo.Vernetzt.Tests.Services;

/// <summary>
/// Retry and circuit breaker of the SMTP send against real sockets on localhost (no SMTP server needed).
/// </summary>
[TestClass]
public class SmtpSenderTests
{
    [TestMethod]
    public void Registration_IsSingleton_SoTheBreakerStateSurvivesRequests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(Config(port: 25));
        services.AddSingleton(new Mock<IAppSettingsService>().Object);
        services.AddInfrastructureEmailSender();
        using var provider = services.BuildServiceProvider(validateScopes: true);

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        Assert.AreSame(
            first.ServiceProvider.GetRequiredService<ISmtpSender>(),
            second.ServiceProvider.GetRequiredService<ISmtpSender>());
    }

    [TestMethod]
    public async Task ConnectionRefused_IsRetried_AndOpensTheBreakerForLaterSends()
    {
        using var recorder = new MetricsRecorder();
        var logger = new Mock<ILogger<SmtpSender>>();
        var sender = new SmtpSender(Config(ClosedPort(), maxRetries: 1, breakerThreshold: 2), logger.Object, recorder.Metrics);

        await Assert.ThrowsAsync<SocketException>(() => sender.SendAsync(Message()));
        // Second send (e.g. the next request): rejected at once, the server is not contacted again.
        await Assert.ThrowsAsync<BrokenCircuitException>(() => sender.SendAsync(Message()));

        VerifyLogged(logger, LogLevel.Warning, "retry attempt", Times.Once());
        Assert.AreEqual(1, recorder.Sum("vernetzt.email.circuit_breaker.opened"));
        Assert.AreEqual(2, recorder.Sum("vernetzt.email.sends", ("outcome", "failure")));
    }

    [TestMethod]
    public async Task ServerThatDoesNotAnswer_FailsWithTimeoutException()
    {
        using var silentServer = new TcpListener(IPAddress.Loopback, 0);
        silentServer.Start();
        var port = ((IPEndPoint)silentServer.LocalEndpoint).Port;
        var sender = new SmtpSender(Config(port, maxRetries: 0, timeoutMs: 300), new Mock<ILogger<SmtpSender>>().Object);

        await Assert.ThrowsAsync<TimeoutException>(() => sender.SendAsync(Message()));
    }

    [TestMethod]
    public async Task CancellationByCaller_IsNotCountedAsFailure()
    {
        using var recorder = new MetricsRecorder();
        using var silentServer = new TcpListener(IPAddress.Loopback, 0);
        silentServer.Start();
        var port = ((IPEndPoint)silentServer.LocalEndpoint).Port;
        var sender = new SmtpSender(Config(port, maxRetries: 0, timeoutMs: 10_000), new Mock<ILogger<SmtpSender>>().Object, recorder.Metrics);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAsync<OperationCanceledException>(() => sender.SendAsync(Message(), cts.Token));

        Assert.AreEqual(0, recorder.Sum("vernetzt.email.sends"));
    }

    private static IConfiguration Config(int port, int maxRetries = 0, int breakerThreshold = 5, int timeoutMs = 5000) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:Smtp:Host"] = "127.0.0.1",
                ["Email:Smtp:Port"] = port.ToString(),
                ["Email:Smtp:UseSsl"] = "false",
                ["Email:Smtp:TimeoutMs"] = timeoutMs.ToString(),
                ["Email:Retry:MaxRetries"] = maxRetries.ToString(),
                ["Email:Retry:InitialDelaySeconds"] = "0",
                ["Email:Retry:CircuitBreakerFailureThreshold"] = breakerThreshold.ToString(),
                ["Email:Retry:CircuitBreakerDurationSeconds"] = "60",
                ["Email:From:Address"] = "system@vernetzt.local",
                ["App:BaseUrl"] = "https://vernetzt.local"
            })
            .Build();

    private static int ClosedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static MailMessage Message() =>
        new("system@vernetzt.local", "empfaenger@example.com", "Test", "Hallo");

    private static void VerifyLogged(Mock<ILogger<SmtpSender>> logger, LogLevel level, string text, Times times) =>
        logger.Verify(l => l.Log(
            level,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => state != null && state.ToString()!.Contains(text)),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), times);
}
