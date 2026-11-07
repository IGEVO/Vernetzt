using System.ComponentModel.DataAnnotations;
using Igevo.Vernetzt.Core.Enums;

namespace Igevo.Vernetzt.Core.Entities;

/// <summary>
/// Audit log entry for tracking all data modifications.
/// </summary>
public class AuditLog : BaseEntity
{
    /// <summary>
    /// Type of entity that was modified (e.g., "Club", "User", "Department").
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// ID of the entity that was modified.
    /// </summary>
    [Required]
    public Guid EntityId { get; set; }

    /// <summary>
    /// Type of action performed.
    /// </summary>
    [Required]
    public AuditAction Action { get; set; }

    /// <summary>
    /// User ID who performed the action.
    /// </summary>
    [Required]
    public Guid UserId { get; set; }

    /// <summary>
    /// Timestamp when the action occurred (UTC).
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// IP address from which the action was performed.
    /// </summary>
    [MaxLength(50)]
    public string? IpAddress { get; set; }

    /// <summary>
    /// JSON representation of the old values (for updates).
    /// </summary>
    public string? OldValues { get; set; }

    /// <summary>
    /// JSON representation of the new values.
    /// </summary>
    public string? NewValues { get; set; }

    /// <summary>
    /// Additional notes or description of the action.
    /// </summary>
    [MaxLength(500)]
    public string? Notes { get; set; }
}
