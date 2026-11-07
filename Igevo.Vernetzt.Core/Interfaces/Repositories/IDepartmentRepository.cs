using Igevo.Vernetzt.Core.Entities;

namespace Igevo.Vernetzt.Core.Interfaces.Repositories;

/// <summary>
/// Repository interface for Department entities.
/// </summary>
public interface IDepartmentRepository : IRepository<Department>
{
    /// <summary>
    /// Get all departments and groups for a specific club (groups include their parent department).
    /// </summary>
    Task<IEnumerable<Department>> GetByClubIdAsync(Guid clubId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all departments with contact assignments included.
    /// </summary>
    Task<IEnumerable<Department>> GetAllWithDetailsAsync(CancellationToken cancellationToken = default);
}
