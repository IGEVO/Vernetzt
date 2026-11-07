using Igevo.Vernetzt.Core.Entities;

namespace Igevo.Vernetzt.Core.Interfaces.Repositories;

/// <summary>
/// Repository interface for ClubType entities.
/// </summary>
public interface IClubTypeRepository : IRepository<ClubType>
{
    /// <summary>
    /// Get club type by name.
    /// </summary>
    Task<ClubType?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
}
