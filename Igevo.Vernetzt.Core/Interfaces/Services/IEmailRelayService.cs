using Igevo.Vernetzt.Core.DTOs.EmailRelay;

namespace Igevo.Vernetzt.Core.Interfaces.Services;

/// <summary>
/// Service for managing email relay tokens and processing incoming relay emails.
/// </summary>
public interface IEmailRelayService
{
    /// <summary>
    /// Creates a new relay token with a unique email address for the specified recipients.
    /// </summary>
    /// <param name="request">Token creation request with recipient user IDs</param>
    /// <param name="userId">ID of the user creating the token</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Response with the generated email address and expiry time</returns>
    Task<CreateEmailRelayTokenResponse> CreateTokenAsync(
        CreateEmailRelayTokenRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes incoming emails in the monitored mailbox.
    /// Matches emails to active tokens, forwards to recipients, sends summary replies,
    /// and cleans up the mailbox.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ProcessIncomingEmailsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes expired and unused relay tokens.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task CleanupExpiredTokensAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes messages from the monitored mailbox that stayed longer than the configured retention.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task CleanupMailboxAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active (non-expired, non-used) tokens for a specific user.
    /// </summary>
    /// <param name="userId">User ID to get tokens for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of active tokens</returns>
    Task<List<EmailRelayTokenDto>> GetActiveTokensAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels (soft-deletes) a relay token by ID.
    /// </summary>
    /// <param name="tokenId">ID of the token to cancel</param>
    /// <param name="userId">ID of the user requesting cancellation (must be owner)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the token was found and cancelled</returns>
    Task<bool> CancelTokenAsync(
        Guid tokenId,
        Guid userId,
        CancellationToken cancellationToken = default);
}
