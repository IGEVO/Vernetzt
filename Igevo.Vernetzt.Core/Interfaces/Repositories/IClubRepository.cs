using Igevo.Vernetzt.Core.Entities;

namespace Igevo.Vernetzt.Core.Interfaces.Repositories;

/// <summary>
/// Repository interface for Club entities with additional query methods.
/// </summary>
public interface IClubRepository : IRepository<Club>
{
    /// <summary>
    /// Search clubs by name.
    /// </summary>
    Task<IEnumerable<Club>> SearchByNameAsync(string searchTerm, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get clubs by club type ID.
    /// </summary>
    Task<IEnumerable<Club>> GetByClubTypeIdAsync(Guid clubTypeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all clubs with departments and contact assignments included.
    /// </summary>
    Task<IEnumerable<Club>> GetAllWithDetailsAsync(CancellationToken cancellationToken = default);
}
