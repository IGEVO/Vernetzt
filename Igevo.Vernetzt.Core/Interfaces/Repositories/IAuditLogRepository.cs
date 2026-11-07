using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;

namespace Igevo.Vernetzt.Core.Interfaces.Repositories;

/// <summary>
/// Repository interface for AuditLog entities.
/// Note: Does not inherit from IRepository because audit logs are immutable
/// (no update/delete operations allowed).
/// </summary>
public interface IAuditLogRepository
{
    /// <summary>
    /// Get audit log by ID.
    /// </summary>
    Task<AuditLog?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all audit logs.
    /// </summary>
    Task<IEnumerable<AuditLog>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an audit log entry.
    /// </summary>
    Task LogAsync(
        string entityType,
        Guid entityId,
        AuditAction action,
        Guid userId,
        string? oldValues,
        string? newValues,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an audit log entry from an AuditLog object.
    /// </summary>
    Task LogAsync(AuditLog auditLog, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get audit logs for a specific entity.
    /// </summary>
    Task<IEnumerable<AuditLog>> GetByEntityAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get audit logs for a specific user.
    /// </summary>
    Task<IEnumerable<AuditLog>> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get recent audit logs (limited count).
    /// </summary>
    Task<IEnumerable<AuditLog>> GetRecentAsync(int count, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete all audit logs older than the specified date.
    /// Returns the number of deleted entries.
    /// </summary>
    Task<int> DeleteOlderThanAsync(DateTime cutoffDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete all audit logs for a specific user.
    /// Returns the number of deleted entries.
    /// </summary>
    Task<int> DeleteByUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
