using Igevo.Vernetzt.Core.Entities;

namespace Igevo.Vernetzt.Core.Interfaces.Services;

/// <summary>
/// Service for managing club types (admin-managed).
/// </summary>
public interface IClubTypeService
{
    /// <summary>
    /// Gets all club types.
    /// </summary>
    Task<IEnumerable<ClubType>> GetAllActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all club types (SystemAdmin only).
    /// </summary>
    Task<IEnumerable<ClubType>> GetAllAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a club type by ID.
    /// </summary>
    Task<ClubType?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new club type (SystemAdmin only).
    /// </summary>
    Task<ClubType> CreateAsync(Guid userId, ClubType clubType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing club type (SystemAdmin only).
    /// </summary>
    Task<ClubType?> UpdateAsync(Guid userId, Guid id, ClubType clubType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a club type (SystemAdmin only).
    /// </summary>
    Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken cancellationToken = default);
}
