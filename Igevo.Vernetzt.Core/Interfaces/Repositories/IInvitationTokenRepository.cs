using Igevo.Vernetzt.Core.Entities;

namespace Igevo.Vernetzt.Core.Interfaces.Repositories;

/// <summary>
/// Repository interface for InvitationToken entities.
/// </summary>
public interface IInvitationTokenRepository
{
    Task<InvitationToken?> GetByTokenAsync(string token, CancellationToken cancellationToken = default);
    Task<InvitationToken?> GetValidByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all valid (non-used, non-revoked, non-expired) invitations for an email, including navigation properties.
    /// </summary>
    Task<List<InvitationToken>> GetAllValidByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<InvitationToken> AddAsync(InvitationToken invitationToken, CancellationToken cancellationToken = default);
    Task<InvitationToken> UpdateAsync(InvitationToken invitationToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all expired or used/revoked invitation tokens older than the specified cutoff date.
    /// Returns the number of deleted entries.
    /// </summary>
    Task<int> DeleteExpiredAsync(DateTime cutoffDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all invitation tokens sent to the specified email address.
    /// Returns the number of deleted entries.
    /// </summary>
    Task<int> DeleteByRecipientAddressAsync(string email, CancellationToken cancellationToken = default);
}
