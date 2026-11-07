using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Igevo.Vernetzt.Infrastructure.Repositories;

/// <summary>
/// EF Core repository implementation for Department entities.
/// </summary>
public class DepartmentRepository : EfCoreRepository<Department>, IDepartmentRepository
{
    public DepartmentRepository(VernetztDbContext context, ILogger<DepartmentRepository> logger)
        : base(context, logger)
    {
    }

    public override async Task<Department?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .AsSplitQuery()
            .Include(d => d.Club)
            .Include(d => d.ParentDepartment)
                .ThenInclude(p => p!.ContactAssignments)
            .Include(d => d.Groups.OrderBy(g => g.Name))
                .ThenInclude(g => g.ContactAssignments)
            .Include(d => d.ContactAssignments)
                .ThenInclude(ca => ca.User)
            .Include(d => d.ContactAssignments)
                .ThenInclude(ca => ca.ContactPosition)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Department>> GetByClubIdAsync(Guid clubId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(d => d.ParentDepartment)
            .Include(d => d.ContactAssignments)
                .ThenInclude(ca => ca.User)
            .Include(d => d.ContactAssignments)
                .ThenInclude(ca => ca.ContactPosition)
            .Where(d => d.ClubId == clubId)
            .OrderBy(d => d.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Department>> GetAllWithDetailsAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(d => d.Club)
            .Include(d => d.ParentDepartment)
            .Include(d => d.ContactAssignments)
                .ThenInclude(ca => ca.User)
            .Include(d => d.ContactAssignments)
                .ThenInclude(ca => ca.ContactPosition)
            .OrderBy(d => d.Name)
            .ToListAsync(cancellationToken);
    }
}
