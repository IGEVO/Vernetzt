using Igevo.Vernetzt.Core.DTOs.Admin;

namespace Igevo.Vernetzt.Core.Interfaces.Services;

/// <summary>
/// Administration overview of open invitations, pending assignment consents and unconfirmed
/// role/office addresses. Only SystemAdmin and Manager may use it.
/// </summary>
public interface IPendingItemsService
{
    /// <summary>
    /// Gets all pending items.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">The requesting user is neither SystemAdmin nor Manager.</exception>
    Task<PendingItemsOverviewDto> GetOverviewAsync(Guid requestingUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes an open invitation. Returns false if it does not exist or is no longer open.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">The requesting user is neither SystemAdmin nor Manager.</exception>
    Task<bool> RevokeInvitationAsync(Guid requestingUserId, Guid invitationId, CancellationToken cancellationToken = default);
}
