using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Igevo.Vernetzt.Infrastructure.Repositories;

/// <summary>
/// EF Core repository implementation for Club entities.
/// </summary>
public class ClubRepository : EfCoreRepository<Club>, IClubRepository
{
    public ClubRepository(VernetztDbContext context, ILogger<ClubRepository> logger)
        : base(context, logger)
    {
    }

    public override async Task<Club?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(c => c.ClubType)
            .Include(c => c.ContactAssignments)
                .ThenInclude(ca => ca.User)
            .Include(c => c.ContactAssignments)
                .ThenInclude(ca => ca.ContactPosition)
            .Include(c => c.Departments)
            .Include(c => c.Locations)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Club>> SearchByNameAsync(string searchTerm, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(c => c.ClubType)
            .Where(c => EF.Functions.Like(c.Name, $"%{searchTerm}%"))
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Club>> GetByClubTypeIdAsync(Guid clubTypeId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(c => c.ClubType)
            .Where(c => c.ClubTypeId == clubTypeId)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Get all clubs with their departments and contact assignments.
    /// </summary>
    public async Task<IEnumerable<Club>> GetAllWithDetailsAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(c => c.ClubType)
            .Include(c => c.Departments)
            .Include(c => c.ContactAssignments)
                .ThenInclude(ca => ca.User)
            .Include(c => c.ContactAssignments)
                .ThenInclude(ca => ca.ContactPosition)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Get clubs for overview/summary (optimized query without details).
    /// </summary>
    public async Task<IEnumerable<Club>> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(c => c.ClubType)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);
    }
}
