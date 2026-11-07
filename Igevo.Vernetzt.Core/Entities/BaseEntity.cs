namespace Igevo.Vernetzt.Core.Entities;

/// <summary>
/// Base entity class with common properties for all entities.
/// </summary>
public abstract class BaseEntity
{
    /// <summary>
    /// Unique identifier for the entity.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Timestamp when the entity was created (UTC).
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the entity was last updated (UTC).
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// User ID who created this entity.
    /// </summary>
    public Guid? CreatedById { get; set; }

    /// <summary>
    /// User ID who last updated this entity.
    /// </summary>
    public Guid? UpdatedById { get; set; }
}
