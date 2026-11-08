using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Igevo.Vernetzt.Infrastructure.Repositories;

/// <summary>
/// EF Core repository implementation for AuditLog entities.
/// AuditLog entries are permanent records.
/// </summary>
public class AuditLogRepository : IAuditLogRepository
{
    private readonly VernetztDbContext _context;
    private readonly DbSet<AuditLog> _dbSet;
    private readonly ILogger<AuditLogRepository> _logger;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public AuditLogRepository(
        VernetztDbContext context,
        ILogger<AuditLogRepository> logger,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _context = context;
        _dbSet = _context.Set<AuditLog>();
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task LogAsync(
        string entityType,
        Guid entityId,
        AuditAction action,
        Guid userId,
        string? oldValues,
        string? newValues,
        CancellationToken cancellationToken = default)
    {
        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            UserId = userId,
            Timestamp = DateTime.UtcNow,
            IpAddress = GetCurrentIpAddress(),
            OldValues = oldValues,
            NewValues = newValues
        };

        await _dbSet.AddAsync(auditLog, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogDebug("Audit log created: {Action} on {EntityType} {EntityId} by user {UserId}",
            action, entityType, entityId, userId);
    }

    public async Task LogAsync(AuditLog auditLog, CancellationToken cancellationToken = default)
    {
        if (auditLog.Id == Guid.Empty)
        {
            auditLog.Id = Guid.NewGuid();
        }
        if (auditLog.Timestamp == default)
        {
            auditLog.Timestamp = DateTime.UtcNow;
        }
        if (string.IsNullOrWhiteSpace(auditLog.IpAddress))
        {
            auditLog.IpAddress = GetCurrentIpAddress();
        }

        await _dbSet.AddAsync(auditLog, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogDebug("Audit log created: {Action} on {EntityType} {EntityId} by user {UserId}",
            auditLog.Action, auditLog.EntityType, auditLog.EntityId, auditLog.UserId);
    }

    public async Task<IEnumerable<AuditLog>> GetByEntityAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(a => a.EntityType == entityType && a.EntityId == entityId)
            .OrderByDescending(a => a.Timestamp)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<AuditLog>> GetForDataSubjectAsync(
        Guid userId,
        IReadOnlyCollection<Guid> relatedEntityIds,
        CancellationToken cancellationToken = default)
    {
        var entityIds = relatedEntityIds.Append(userId).Distinct().ToList();
        return await _dbSet
            .Where(a => a.UserId == userId || entityIds.Contains(a.EntityId))
            .OrderByDescending(a => a.Timestamp)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<AuditLog>> GetRecentAsync(int count, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .OrderByDescending(a => a.Timestamp)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    public async Task<AuditLog?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet.FindAsync([id], cancellationToken);
    }

    public async Task<IEnumerable<AuditLog>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet.ToListAsync(cancellationToken);
    }

    public async Task<int> DeleteOlderThanAsync(DateTime cutoffDate, CancellationToken cancellationToken = default)
    {
        var oldEntries = await _dbSet
            .Where(a => a.Timestamp < cutoffDate)
            .ToListAsync(cancellationToken);

        if (oldEntries.Count == 0)
            return 0;

        _dbSet.RemoveRange(oldEntries);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted {Count} audit log entries older than {CutoffDate}", oldEntries.Count, cutoffDate);
        return oldEntries.Count;
    }

    public async Task<int> DeleteByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var userEntries = await _dbSet
            .Where(a => a.UserId == userId)
            .ToListAsync(cancellationToken);

        if (userEntries.Count == 0)
            return 0;

        _dbSet.RemoveRange(userEntries);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted {Count} audit log entries for user {UserId}", userEntries.Count, userId);
        return userEntries.Count;
    }

    private string? GetCurrentIpAddress()
    {
        return _httpContextAccessor?.HttpContext?.Connection?.RemoteIpAddress?.ToString();
    }
}

