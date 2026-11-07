namespace Igevo.Vernetzt.Core.Interfaces.Services;

/// <summary>
/// Service for cleaning up expired/used data from the database.
/// Handles audit log retention, expired token cleanup, and GDPR-related data removal.
/// </summary>
public interface IDataCleanupService
{
    /// <summary>
    /// Runs all cleanup tasks: expired tokens, old audit logs, used magic links, etc.
    /// Returns a summary of how many records were deleted per category.
    /// </summary>
    Task<DataCleanupResult> RunCleanupAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a data cleanup run.
/// </summary>
public class DataCleanupResult
{
    public int DeletedAuditLogs { get; set; }
    public int DeletedInvitationTokens { get; set; }
    public int DeletedEmailRelayTokens { get; set; }
    public int DeletedConsumedLoginTokens { get; set; }
    public int DeletedCardDavTokens { get; set; }
    public DateTime CleanupTimestamp { get; set; } = DateTime.UtcNow;

    public int TotalDeleted => DeletedAuditLogs + DeletedInvitationTokens +
        DeletedEmailRelayTokens + DeletedConsumedLoginTokens + DeletedCardDavTokens;
}
