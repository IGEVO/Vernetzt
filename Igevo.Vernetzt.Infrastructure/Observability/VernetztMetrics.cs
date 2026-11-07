using System.Diagnostics.Metrics;

namespace Igevo.Vernetzt.Infrastructure.Observability;

/// <summary>
/// Business metrics of Vernetzt (meter <see cref="MeterName"/>), exported via OpenTelemetry.
/// Tag values are fixed, low-cardinality strings; never put user data (ids, addresses) into tags.
/// In Prometheus the names appear with underscores and a <c>_total</c> suffix for counters,
/// e.g. <c>vernetzt_auth_login_link_rejected_total</c>.
/// </summary>
public sealed class VernetztMetrics
{
    public const string MeterName = "Igevo.Vernetzt";

    private readonly Meter _meter;
    private readonly Counter<long> _loginLinksIssued;
    private readonly Counter<long> _loginLinksRejected;
    private readonly Counter<long> _passkeyFailures;
    private readonly Counter<long> _emailSends;
    private readonly Counter<long> _smtpCircuitBreakerOpened;
    private readonly Counter<long> _backups;
    private readonly Counter<long> _relayEmails;

    public VernetztMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);

        _loginLinksIssued = _meter.CreateCounter<long>(
            "vernetzt.auth.login_link.issued", "{link}", "Login link tokens issued");
        _loginLinksRejected = _meter.CreateCounter<long>(
            "vernetzt.auth.login_link.rejected", "{link}", "Login link requests or redemptions rejected, by reason");
        _passkeyFailures = _meter.CreateCounter<long>(
            "vernetzt.auth.passkey.failures", "{attempt}", "Failed passkey registrations and sign-ins, by operation");
        _emailSends = _meter.CreateCounter<long>(
            "vernetzt.email.sends", "{email}", "SMTP send attempts after retries, by outcome");
        _smtpCircuitBreakerOpened = _meter.CreateCounter<long>(
            "vernetzt.email.circuit_breaker.opened", "{event}", "Times the SMTP circuit breaker opened");
        _backups = _meter.CreateCounter<long>(
            "vernetzt.backup.runs", "{backup}", "Database backup runs, by trigger and outcome");
        _relayEmails = _meter.CreateCounter<long>(
            "vernetzt.relay.emails", "{email}", "Incoming relay emails, by outcome");
    }

    public static class RejectReasons
    {
        public const string UnknownEmail = "unknown_email";
        public const string BudgetExhausted = "budget_exhausted";
        public const string InvalidOrExpired = "invalid_or_expired";
        public const string AlreadyUsed = "already_used";
    }

    public static class RelayOutcomes
    {
        public const string Forwarded = "forwarded";
        public const string ForwardFailed = "forward_failed";
        public const string DiscardedTokenExpired = "discarded_token_expired";
        public const string DiscardedNoToken = "discarded_no_token";
        public const string RejectedTooLarge = "rejected_too_large";
        public const string Error = "error";
    }

    public void LoginLinkIssued() => _loginLinksIssued.Add(1);

    public void LoginLinkRejected(string reason) =>
        _loginLinksRejected.Add(1, new KeyValuePair<string, object?>("reason", reason));

    /// <param name="operation"><c>registration</c> or <c>signin</c>.</param>
    public void PasskeyFailed(string operation) =>
        _passkeyFailures.Add(1, new KeyValuePair<string, object?>("operation", operation));

    public void EmailSendCompleted(bool success) =>
        _emailSends.Add(1, new KeyValuePair<string, object?>("outcome", success ? "success" : "failure"));

    public void SmtpCircuitBreakerOpened() => _smtpCircuitBreakerOpened.Add(1);

    public void BackupCompleted(string trigger, bool success) =>
        _backups.Add(1,
            new KeyValuePair<string, object?>("trigger", trigger.ToLowerInvariant()),
            new KeyValuePair<string, object?>("outcome", success ? "success" : "failure"));

    public void RelayEmailProcessed(string outcome) =>
        _relayEmails.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    /// <summary>
    /// Publishes the Unix time (seconds) of the newest backup and of the last failed attempt as gauges.
    /// The callback runs on each metrics export; it returns null values when nothing is known.
    /// </summary>
    public void ObserveBackupState(Func<(DateTime? LastSuccessUtc, DateTime? LastFailureUtc)> getState)
    {
        _meter.CreateObservableGauge(
            "vernetzt.backup.last_success_timestamp",
            () => ToMeasurements(getState().LastSuccessUtc),
            "s",
            "Unix time of the newest database backup");
        _meter.CreateObservableGauge(
            "vernetzt.backup.last_failure_timestamp",
            () => ToMeasurements(getState().LastFailureUtc),
            "s",
            "Unix time of the last failed database backup since the app started");
    }

    private static IEnumerable<Measurement<double>> ToMeasurements(DateTime? utc) =>
        utc is { } value
            ? [new Measurement<double>(new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToUnixTimeMilliseconds() / 1000d)]
            : [];
}
