using Igevo.Vernetzt.Core.Entities;

namespace Igevo.Vernetzt.Core.Interfaces.Services;

/// <summary>
/// Service for managing clubs.
/// </summary>
public interface IClubService
{
    /// <summary>
    /// Gets all clubs accessible to the user.
    /// </summary>
    Task<IEnumerable<Club>> GetAccessibleClubsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a club by ID if user has access.
    /// </summary>
    Task<Club?> GetByIdAsync(Guid userId, Guid clubId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new club (SystemAdmin only).
    /// </summary>
    Task<Club> CreateAsync(Guid userId, Club club, CancellationToken cancellationToken = default, IReadOnlyCollection<Guid>? locationIds = null);

    /// <summary>
    /// Updates an existing club if user has edit permission.
    /// </summary>
    Task<Club?> UpdateAsync(Guid userId, Guid clubId, Club club, CancellationToken cancellationToken = default, IReadOnlyCollection<Guid>? locationIds = null);

    /// <summary>
    /// Deletes a club (SystemAdmin/Manager only).
    /// </summary>
    Task<bool> DeleteAsync(Guid userId, Guid clubId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a contact person to a club with optional position.
    /// </summary>
    Task<bool> AddContactPersonAsync(
        Guid userId,
        Guid clubId,
        Guid contactPersonId,
        Guid? positionId = null,
        bool canEdit = false,
        string? notes = null,
        bool receivesLocationInfo = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the position and edit rights of an existing contact person.
    /// </summary>
    Task<bool> UpdateContactPersonAsync(
        Guid userId,
        Guid clubId,
        Guid contactPersonId,
        Guid? positionId,
        bool canEdit = false,
        string? notes = null,
        bool receivesLocationInfo = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a contact person from a club.
    /// </summary>
    Task<bool> RemoveContactPersonAsync(Guid userId, Guid clubId, Guid contactPersonId, CancellationToken cancellationToken = default);
}
