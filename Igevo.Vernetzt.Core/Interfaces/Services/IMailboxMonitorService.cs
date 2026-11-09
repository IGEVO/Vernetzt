using Igevo.Vernetzt.Core.DTOs.EmailRelay;

namespace Igevo.Vernetzt.Core.Interfaces.Services;

/// <summary>
/// Service for monitoring a mailbox (IMAP or MailDev REST API) for incoming emails.
/// </summary>
public interface IMailboxMonitorService
{
    /// <summary>
    /// Fetches all new emails from the monitored mailbox.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of incoming emails found in the mailbox</returns>
    Task<List<IncomingEmail>> FetchNewEmailsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an email from the monitored mailbox after processing.
    /// </summary>
    /// <param name="messageId">Provider-specific message ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task DeleteEmailAsync(string messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all messages that have been in the mailbox longer than <paramref name="minAge"/>,
    /// regardless of whether they were processed (oversized, failed, unmatched).
    /// </summary>
    /// <param name="minAge">Minimum age a message must have to be deleted</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of deleted messages</returns>
    Task<int> DeleteOlderThanAsync(TimeSpan minAge, CancellationToken cancellationToken = default);
}
