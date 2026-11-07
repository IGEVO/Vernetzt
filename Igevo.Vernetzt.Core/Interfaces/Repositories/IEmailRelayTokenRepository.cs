using Igevo.Vernetzt.Core.Entities;

namespace Igevo.Vernetzt.Core.Interfaces.Repositories;

/// <summary>
/// Repository interface for EmailRelayToken entities.
/// </summary>
public interface IEmailRelayTokenRepository
{
    /// <summary>
    /// Gets an active (non-expired, non-used, non-deleted) token by its email address.
    /// </summary>
    Task<EmailRelayToken?> GetActiveTokenByAddressAsync(string tokenAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an expired or used token by its email address (for failure notifications).
    /// </summary>
    Task<EmailRelayToken?> GetTokenByAddressAsync(string tokenAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active (non-expired, non-used) tokens for a specific user.
    /// </summary>
    Task<List<EmailRelayToken>> GetActiveTokensByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all expired and unused tokens that should be cleaned up.
    /// </summary>
    Task<List<EmailRelayToken>> GetExpiredTokensAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a token by ID.
    /// </summary>
    Task<EmailRelayToken?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new relay token.
    /// </summary>
    Task<EmailRelayToken> AddAsync(EmailRelayToken token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing relay token.
    /// </summary>
    Task<EmailRelayToken> UpdateAsync(EmailRelayToken token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes a relay token.
    /// </summary>
    Task DeleteAsync(EmailRelayToken token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts active (non-expired, non-used) tokens for a specific user.
    /// </summary>
    Task<int> CountActiveTokensByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all expired or used email relay tokens older than the specified cutoff date.
    /// Returns the number of deleted entries.
    /// </summary>
    Task<int> DeleteExpiredAsync(DateTime cutoffDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all email relay tokens created by the specified user.
    /// Returns the number of deleted entries.
    /// </summary>
    Task<int> DeleteByUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
