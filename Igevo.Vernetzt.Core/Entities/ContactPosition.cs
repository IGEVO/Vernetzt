using System.ComponentModel.DataAnnotations;

namespace Igevo.Vernetzt.Core.Entities;

/// <summary>
/// Represents a position/role that a contact person can have within a club or department.
/// Examples: Vorstand (Board), Kassierer (Treasurer), Trainer (Coach), etc.
/// </summary>
public class ContactPosition : BaseEntity
{
    /// <summary>
    /// Name of the position.
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of the position.
    /// </summary>
    [MaxLength(1000)]
    public string? Description { get; set; }

    // Navigation properties

    /// <summary>
    /// Contact assignments using this position.
    /// </summary>
    public virtual ICollection<ContactAssignment> ContactAssignments { get; set; } = [];
}
