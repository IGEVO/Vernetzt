using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.DTOs.Common;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Igevo.Vernetzt.Infrastructure.Repositories;

/// <summary>
/// EF Core repository implementation for calendar events.
/// </summary>
public class EventRepository : EfCoreRepository<Event>, IEventRepository
{
    public EventRepository(VernetztDbContext context, ILogger<EventRepository> logger)
        : base(context, logger)
    {
    }

    public override async Task<Event?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(e => e.Club)
            .Include(e => e.Department)
                .ThenInclude(d => d!.Club)
            .Include(e => e.Department)
                .ThenInclude(d => d!.ParentDepartment)
            .Include(e => e.Media.OrderBy(m => m.SortOrder))
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Event>> GetByClubAsync(
        Guid clubId,
        bool includeDepartments,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbSet
            .Include(e => e.Club)
            .Include(e => e.Department)
                .ThenInclude(d => d!.Club)
            .Include(e => e.Department)
                .ThenInclude(d => d!.ParentDepartment)
            .Include(e => e.Media.OrderBy(m => m.SortOrder))
            .AsQueryable();

        query = includeDepartments
            ? query.Where(e => e.ClubId == clubId || (e.Department != null && e.Department.ClubId == clubId))
            : query.Where(e => e.ClubId == clubId);

        query = ApplyDateRange(query, fromUtc, toUtc);

        return await query
            .OrderBy(e => e.StartUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Event>> GetByDepartmentAsync(
        Guid departmentId,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbSet
            .Include(e => e.Club)
            .Include(e => e.Department)
                .ThenInclude(d => d!.Club)
            .Include(e => e.Department)
                .ThenInclude(d => d!.ParentDepartment)
            .Include(e => e.Media.OrderBy(m => m.SortOrder))
            // A department calendar includes the events of its groups.
            .Where(e => e.DepartmentId == departmentId ||
                        (e.Department != null && e.Department.ParentDepartmentId == departmentId));

        query = ApplyDateRange(query, fromUtc, toUtc);

        return await query
            .OrderBy(e => e.StartUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Event>> GetPublicGlobalAsync(
        bool highlightsOnly,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbSet
            .Include(e => e.Club)
            .Include(e => e.Department)
                .ThenInclude(d => d!.Club)
            .Include(e => e.Department)
                .ThenInclude(d => d!.ParentDepartment)
            .Include(e => e.Media.OrderBy(m => m.SortOrder))
            .AsQueryable();

        if (highlightsOnly)
        {
            query = query.Where(e => e.IsHighlight);
        }

        query = ApplyDateRange(query, fromUtc, toUtc);

        return await query
            .OrderBy(e => e.StartUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<Event>> GetByClubPagedAsync(
        Guid clubId,
        bool includeDepartments,
        int page,
        int pageSize,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedPage = page < 1 ? 1 : page;
        var normalizedPageSize = pageSize < 1 ? 20 : Math.Min(pageSize, 100);

        var query = _dbSet
            .Include(e => e.Club)
            .Include(e => e.Department)
                .ThenInclude(d => d!.Club)
            .Include(e => e.Department)
                .ThenInclude(d => d!.ParentDepartment)
            .Include(e => e.Media.OrderBy(m => m.SortOrder))
            .AsQueryable();

        query = includeDepartments
            ? query.Where(e => e.ClubId == clubId || (e.Department != null && e.Department.ClubId == clubId))
            : query.Where(e => e.ClubId == clubId);

        query = ApplyDateRange(query, fromUtc, toUtc);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(e => e.StartUtc)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        return PagedResult<Event>.Create(items, total, normalizedPage, normalizedPageSize);
    }

    public async Task<PagedResult<Event>> GetByDepartmentPagedAsync(
        Guid departmentId,
        int page,
        int pageSize,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedPage = page < 1 ? 1 : page;
        var normalizedPageSize = pageSize < 1 ? 20 : Math.Min(pageSize, 100);

        var query = _dbSet
            .Include(e => e.Club)
            .Include(e => e.Department)
                .ThenInclude(d => d!.Club)
            .Include(e => e.Department)
                .ThenInclude(d => d!.ParentDepartment)
            .Include(e => e.Media.OrderBy(m => m.SortOrder))
            // A department calendar includes the events of its groups.
            .Where(e => e.DepartmentId == departmentId ||
                        (e.Department != null && e.Department.ParentDepartmentId == departmentId));

        query = ApplyDateRange(query, fromUtc, toUtc);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(e => e.StartUtc)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        return PagedResult<Event>.Create(items, total, normalizedPage, normalizedPageSize);
    }

    public async Task<int> CountMediaAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        return await _context.Set<EventMedia>()
            .CountAsync(m => m.EventId == eventId, cancellationToken);
    }

    public async Task<EventMedia?> GetMediaByIdAsync(Guid mediaId, CancellationToken cancellationToken = default)
    {
        return await _context.Set<EventMedia>()
            .FirstOrDefaultAsync(m => m.Id == mediaId, cancellationToken);
    }

    public async Task<EventMedia> AddMediaAsync(EventMedia media, CancellationToken cancellationToken = default)
    {
        _context.Set<EventMedia>().Add(media);
        await _context.SaveChangesAsync(cancellationToken);
        return media;
    }

    public async Task<bool> DeleteMediaAsync(Guid mediaId, CancellationToken cancellationToken = default)
    {
        var media = await _context.Set<EventMedia>().FindAsync([mediaId], cancellationToken);
        if (media == null)
        {
            return false;
        }

        _context.Set<EventMedia>().Remove(media);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<EventMedia>> GetMediaByEventAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        return await _context.Set<EventMedia>()
            .Where(m => m.EventId == eventId)
            .OrderBy(m => m.SortOrder)
            .ToListAsync(cancellationToken);
    }

    public async Task UpdateMediaAsync(IEnumerable<EventMedia> media, CancellationToken cancellationToken = default)
    {
        foreach (var item in media)
        {
            item.UpdatedAt = DateTime.UtcNow;
            if (_context.Entry(item).State == EntityState.Detached)
            {
                _context.Set<EventMedia>().Update(item);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private static IQueryable<Event> ApplyDateRange(IQueryable<Event> query, DateTime? fromUtc, DateTime? toUtc)
    {
        if (fromUtc.HasValue)
        {
            query = query.Where(e => e.EndUtc >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(e => e.StartUtc <= toUtc.Value);
        }

        return query;
    }
}
