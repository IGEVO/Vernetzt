using System.ComponentModel.DataAnnotations;
using Igevo.Vernetzt.Core.Enums;

namespace Igevo.Vernetzt.Core.Entities;

/// <summary>
/// Represents a department within a club (e.g., "Handball") or a group within a department
/// (e.g., "Bambini", "1. Mannschaft"). Groups are departments with a <see cref="ParentDepartmentId"/>;
/// the hierarchy is limited to one level: club &gt; department &gt; group.
/// </summary>
public class Department : BaseEntity
{
    /// <summary>
    /// Name of the department.
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional abbreviation, e.g. "FB" for "Fußball" (see <see cref="Club.ShortName"/>).
    /// </summary>
    [MaxLength(Club.MaxShortNameLength)]
    public string? ShortName { get; set; }

    /// <summary>
    /// Public free-text information, e.g. training times. Plain text, line breaks are preserved.
    /// </summary>
    [MaxLength(Club.MaxDescriptionLength)]
    public string? Description { get; set; }

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
    /// Department website URL.
    /// </summary>
    [Url]
    [MaxLength(500)]
    public string? WebsiteUrl { get; set; }

    /// <summary>
    /// Department email address.
    /// </summary>
    [EmailAddress]
    [MaxLength(255)]
    public string? Email { get; set; }

    /// <summary>
    /// Links to social media profiles (stored as JSON). Publicly visible like the website.
    /// </summary>
    public List<SocialMediaLink> SocialMediaLinks { get; set; } = [];


    /// <summary>
    /// ID of the club this department belongs to.
    /// </summary>
    [Required]
    public Guid ClubId { get; set; }

    /// <summary>
    /// Navigation property to the parent club.
    /// </summary>
    public virtual Club Club { get; set; } = null!;

    /// <summary>
    /// Parent department if this entry is a group. Null for top-level departments.
    /// A group always belongs to the same club as its parent department.
    /// </summary>
    public Guid? ParentDepartmentId { get; set; }

    /// <summary>
    /// Navigation property to the parent department (only set for groups).
    /// </summary>
    public virtual Department? ParentDepartment { get; set; }

    /// <summary>
    /// True if this entry is a group below a department.
    /// </summary>
    public bool IsGroup => ParentDepartmentId.HasValue;

    /// <summary>
    /// Whether the department has an avatar uploaded.
    /// If false, the club's avatar is used as fallback.
    /// </summary>
    public bool HasAvatar { get; set; } = false;

    // Navigation properties

    /// <summary>
    /// Contact person assignments for this department (User + Position).
    /// </summary>
    public virtual ICollection<ContactAssignment> ContactAssignments { get; set; } = [];

    /// <summary>
    /// Calendar events assigned directly to this department.
    /// </summary>
    public virtual ICollection<Event> Events { get; set; } = [];

    /// <summary>
    /// Groups below this department (empty for groups themselves).
    /// </summary>
    public virtual ICollection<Department> Groups { get; set; } = [];
}
