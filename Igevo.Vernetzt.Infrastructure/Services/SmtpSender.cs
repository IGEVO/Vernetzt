using System.Net.Mail;
using System.Net.Sockets;
using MailKit.Security;
using MimeKit;
using Igevo.Vernetzt.Infrastructure.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using MailKitSmtpClient = MailKit.Net.Smtp.SmtpClient;
using SmtpCommandException = MailKit.Net.Smtp.SmtpCommandException;
using SmtpProtocolException = MailKit.Net.Smtp.SmtpProtocolException;

namespace Igevo.Vernetzt.Infrastructure.Services;

/// <summary>
/// Handles the physical SMTP send with Polly retry + circuit breaker.
/// Registered as Singleton so the circuit breaker sees all sends of the application; each send uses its own
/// MailKit client, so concurrent use is safe. Uses MailKit for proper STARTTLS and SSL-on-connect support.
/// </summary>
internal sealed class SmtpSender : ISmtpSender
{
    private readonly string _smtpHost;
    private readonly int _smtpPort;
    private readonly bool _smtpUseSsl;
    private readonly string? _smtpUsername;
    private readonly string? _smtpPassword;
    private readonly int _smtpTimeoutMs;
    private readonly int _maxRetries;
    private readonly ResiliencePipeline _pipeline;
    private readonly ILogger<SmtpSender> _logger;
    private readonly VernetztMetrics? _metrics;

    public SmtpSender(IConfiguration configuration, ILogger<SmtpSender> logger, VernetztMetrics? metrics = null)
    {
        _logger = logger;
        _metrics = metrics;

        _smtpHost = configuration["Email:Smtp:Host"]
            ?? throw new InvalidOperationException("Email:Smtp:Host is required in configuration");
        _smtpPort = int.Parse(configuration["Email:Smtp:Port"]
            ?? throw new InvalidOperationException("Email:Smtp:Port is required in configuration"));
        _smtpUseSsl = bool.Parse(configuration["Email:Smtp:UseSsl"]
            ?? throw new InvalidOperationException("Email:Smtp:UseSsl is required in configuration"));
        _smtpUsername = configuration["Email:Smtp:Username"];
        _smtpPassword = configuration["Email:Smtp:Password"];
        _smtpTimeoutMs = int.Parse(configuration["Email:Smtp:TimeoutMs"] ?? "30000");

        _maxRetries = int.Parse(configuration["Email:Retry:MaxRetries"] ?? "3");
        var initialDelaySeconds = int.Parse(configuration["Email:Retry:InitialDelaySeconds"] ?? "2");
        var useExponentialBackoff = bool.Parse(configuration["Email:Retry:UseExponentialBackoff"] ?? "true");
        var circuitBreakerFailureThreshold = int.Parse(configuration["Email:Retry:CircuitBreakerFailureThreshold"] ?? "5");
        var circuitBreakerDurationSeconds = int.Parse(configuration["Email:Retry:CircuitBreakerDurationSeconds"] ?? "300");

        var builder = new ResiliencePipelineBuilder();

        // MaxRetries = 0 switches retries off (Polly requires at least one attempt in the options)
        if (_maxRetries > 0)
        {
            builder.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = _maxRetries,
                Delay = TimeSpan.FromSeconds(initialDelaySeconds),
                BackoffType = useExponentialBackoff ? DelayBackoffType.Exponential : DelayBackoffType.Linear,
                ShouldHandle = new PredicateBuilder()
                    .Handle<SmtpCommandException>(IsTransientSmtpError)
                    .Handle<SmtpProtocolException>()
                    .Handle<SocketException>()   // server unreachable, connection refused
                    .Handle<IOException>()       // connection dropped
                    .Handle<TimeoutException>(), // see SendWithPipelineAsync
                OnRetry = args =>
                {
                    _logger.LogWarning(
                        "Email send retry attempt {Attempt}/{MaxAttempts} after {Delay}ms due to: {Exception}",
                        args.AttemptNumber + 1,
                        _maxRetries + 1,
                        args.RetryDelay.TotalMilliseconds,
                        args.Outcome.Exception?.Message);
                    return ValueTask.CompletedTask;
                }
            });
        }

        _pipeline = builder
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                MinimumThroughput = circuitBreakerFailureThreshold,
                BreakDuration = TimeSpan.FromSeconds(circuitBreakerDurationSeconds),
                // Everything that points at the SMTP server or its configuration, including permanent errors
                // (authentication, TLS): stop hammering the server until it has had time to recover.
                ShouldHandle = new PredicateBuilder()
                    .Handle<SmtpCommandException>()
                    .Handle<SmtpProtocolException>()
                    .Handle<SocketException>()
                    .Handle<IOException>()
                    .Handle<TimeoutException>()
                    .Handle<AuthenticationException>()
                    .Handle<SslHandshakeException>(),
                OnOpened = args =>
                {
                    _logger.LogError(
                        "Circuit breaker OPENED: Email service suspended for {Duration}s due to repeated failures",
                        circuitBreakerDurationSeconds);
                    _metrics?.SmtpCircuitBreakerOpened();
                    return ValueTask.CompletedTask;
                },
                OnClosed = args =>
                {
                    _logger.LogInformation("Circuit breaker CLOSED: Email service resumed");
                    return ValueTask.CompletedTask;
                },
                OnHalfOpened = args =>
                {
                    _logger.LogInformation("Circuit breaker HALF-OPEN: Testing email service recovery");
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    public async Task SendAsync(MailMessage message, CancellationToken cancellationToken = default)
    {
        // Convert System.Net.Mail.MailMessage to MimeKit MimeMessage
        var mimeMessage = MimeMessage.CreateFromMailMessage(message);

        try
        {
            await SendWithPipelineAsync(mimeMessage, cancellationToken);
            _metrics?.EmailSendCompleted(success: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _metrics?.EmailSendCompleted(success: false);
            throw;
        }
    }

    private async Task SendWithPipelineAsync(MimeMessage mimeMessage, CancellationToken cancellationToken)
    {
        await _pipeline.ExecuteAsync(async ct =>
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(_smtpTimeoutMs));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            using var smtpClient = new MailKitSmtpClient();

            // Port 465 uses implicit SSL (SMTPS); port 587 uses STARTTLS; no SSL = plain
            var socketOptions = _smtpPort == 465
                ? SecureSocketOptions.SslOnConnect
                : _smtpUseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None;

            try
            {
                await smtpClient.ConnectAsync(_smtpHost, _smtpPort, socketOptions, linkedCts.Token);

                if (!string.IsNullOrWhiteSpace(_smtpUsername) && !string.IsNullOrWhiteSpace(_smtpPassword))
                {
                    await smtpClient.AuthenticateAsync(_smtpUsername, _smtpPassword, linkedCts.Token);
                }

                await smtpClient.SendAsync(mimeMessage, linkedCts.Token);
                await smtpClient.DisconnectAsync(true, linkedCts.Token);
            }
            catch (SmtpCommandException ex)
            {
                _logger.LogError(
                    ex,
                    "SMTP command error: StatusCode={StatusCode}, Message={Message}",
                    ex.StatusCode,
                    ex.Message);
                throw;
            }
            catch (SmtpProtocolException ex)
            {
                _logger.LogError(ex, "SMTP protocol error: {Message}", ex.Message);
                throw;
            }
            catch (OperationCanceledException ex) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                // Our own timeout, not a cancellation by the caller: make it retryable and count it for the breaker.
                throw new TimeoutException($"SMTP server {_smtpHost}:{_smtpPort} did not respond within {_smtpTimeoutMs} ms.", ex);
            }
        }, cancellationToken);
    }

    // 4xx = temporary failures (retry), 5xx = permanent failures (don't retry)
    private static bool IsTransientSmtpError(SmtpCommandException ex) =>
        (int)ex.StatusCode is >= 400 and < 500
        || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("network", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase);
}

