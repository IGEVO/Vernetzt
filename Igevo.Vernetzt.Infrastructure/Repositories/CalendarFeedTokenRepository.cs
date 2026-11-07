using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Igevo.Vernetzt.Infrastructure.Repositories;

/// <summary>
/// EF Core repository for calendar feed tokens.
/// </summary>
public class CalendarFeedTokenRepository : EfCoreRepository<CalendarFeedToken>, ICalendarFeedTokenRepository
{
    public CalendarFeedTokenRepository(VernetztDbContext context, ILogger<CalendarFeedTokenRepository> logger)
        : base(context, logger)
    {
    }

    public override async Task<CalendarFeedToken?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(t => t.Club)
            .Include(t => t.Department)
                .ThenInclude(d => d!.Club)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<CalendarFeedToken?> GetActiveByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(t => t.Club)
            .Include(t => t.Department)
                .ThenInclude(d => d!.Club)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash && !t.IsRevoked, cancellationToken);
    }

    public async Task<IEnumerable<CalendarFeedToken>> GetByClubAsync(Guid clubId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(t => t.Scope == CalendarFeedScope.Club && t.ClubId == clubId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<CalendarFeedToken>> GetByDepartmentAsync(Guid departmentId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(t => t.Scope == CalendarFeedScope.Department && t.DepartmentId == departmentId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<CalendarFeedToken>> GetGlobalAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(t => t.Scope == CalendarFeedScope.Global)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}
