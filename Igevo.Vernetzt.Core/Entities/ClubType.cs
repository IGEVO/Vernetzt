using System.ComponentModel.DataAnnotations;

namespace Igevo.Vernetzt.Core.Entities;

/// <summary>
/// Represents a type/category of club that can be managed by administrators.
/// </summary>
public class ClubType : BaseEntity
{
    /// <summary>
    /// Name of the club type (e.g., "Fußballverein", "Tennisverein", "Mehrspartenverein").
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of this club type.
    /// </summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    // Navigation properties

    /// <summary>
    /// Clubs of this type.
    /// </summary>
    public virtual ICollection<Club> Clubs { get; set; } = [];
}
