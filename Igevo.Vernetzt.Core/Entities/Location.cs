using System.ComponentModel.DataAnnotations;

namespace Igevo.Vernetzt.Core.Entities;

/// <summary>
/// A centrally maintained location ("Standort"), e.g. a sports hall, that clubs, departments
/// and groups can be linked to. Used to address location-specific information (e.g. a hall closure).
/// Locations are not inherited: a location only applies to the organization it is linked to.
/// </summary>
public class Location : BaseEntity
{
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 1000;

    /// <summary>
    /// Unique name of the location (case-insensitive).
    /// </summary>
    [Required]
    [MaxLength(MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional description.
    /// </summary>
    [MaxLength(MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>
    /// Street and house number (optional).
    /// </summary>
    [MaxLength(200)]
    public string? Street { get; set; }

    /// <summary>
    /// Postal code (optional).
    /// </summary>
    [MaxLength(20)]
    public string? PostalCode { get; set; }

    /// <summary>
    /// City name (optional).
    /// </summary>
    [MaxLength(100)]
    public string? City { get; set; }

    // Navigation properties

    /// <summary>
    /// Clubs that use this location.
    /// </summary>
    public virtual ICollection<Club> Clubs { get; set; } = [];

    /// <summary>
    /// Departments and groups that use this location.
    /// </summary>
    public virtual ICollection<Department> Departments { get; set; } = [];
}
