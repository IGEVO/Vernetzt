using System.ComponentModel.DataAnnotations;
using Igevo.Vernetzt.Core.Enums;

namespace Igevo.Vernetzt.Core.Entities;

/// <summary>
/// Represents a club or organization.
/// </summary>
public class Club : BaseEntity
{
    /// <summary>
    /// Maximum length of the public description of clubs, departments and groups.
    /// </summary>
    public const int MaxDescriptionLength = 4000;

    /// <summary>
    /// Name of the club.
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Maximum length of <see cref="ShortName"/> (also used for departments and groups).
    /// </summary>
    public const int MaxShortNameLength = 20;

    /// <summary>
    /// Optional abbreviation, e.g. "TVM" for "Turnverein Musterstadt". Used where space is short,
    /// e.g. next to positions in the CardDAV address book.
    /// </summary>
    [MaxLength(MaxShortNameLength)]
    public string? ShortName { get; set; }

    /// <summary>
    /// Public free-text information about the club. Plain text, line breaks are preserved.
    /// </summary>
    [MaxLength(MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>
    /// ID of the club type (e.g., Football, Tennis, General).
    /// References the ClubType entity.
    /// </summary>
    public Guid? ClubTypeId { get; set; }

    /// <summary>
    /// Navigation property to ClubType.
    /// </summary>
    public virtual ClubType? ClubType { get; set; }

    /// <summary>
    /// Street and house number.
    /// </summary>
    [MaxLength(200)]
    public string? Street { get; set; }

    /// <summary>
    /// Postal code.
    /// </summary>
    [MaxLength(20)]
    public string? PostalCode { get; set; }

    /// <summary>
    /// City name.
    /// </summary>
    [MaxLength(100)]
    public string? City { get; set; }

    /// <summary>
    /// Club's website URL.
    /// </summary>
    [Url]
    [MaxLength(500)]
    public string? WebsiteUrl { get; set; }

    /// <summary>
    /// Club's official email address.
    /// </summary>
    [EmailAddress]
    [MaxLength(255)]
    public string? Email { get; set; }

    /// <summary>
    /// Links to social media profiles (stored as JSON). Publicly visible like the website.
    /// </summary>
    public List<SocialMediaLink> SocialMediaLinks { get; set; } = [];

    /// <summary>
    /// Visibility level for contact persons assigned to this club.
    /// Determines who can see the contact person information.
    /// </summary>
    public VisibilityLevel ContactPersonsVisibility { get; set; } = VisibilityLevel.AllUsers;

    /// <summary>
    /// Whether the club has uploaded an avatar/logo image.
    /// </summary>
    public bool HasAvatar { get; set; } = false;

    // Navigation properties

    /// <summary>
    /// Departments belonging to this club.
    /// </summary>
    public virtual ICollection<Department> Departments { get; set; } = [];

    /// <summary>
    /// Contact person assignments for this club (User + Position).
    /// </summary>
    public virtual ICollection<ContactAssignment> ContactAssignments { get; set; } = [];

    /// <summary>
    /// Calendar events assigned directly to this club.
    /// </summary>
    public virtual ICollection<Event> Events { get; set; } = [];
}
