using System.ComponentModel.DataAnnotations;
using Igevo.Vernetzt.Core.Enums;

namespace Igevo.Vernetzt.Core.Entities;

/// <summary>
/// Represents the assignment of a contact person (User) to a club or department with a specific position.
/// A contact assignment must belong to either a Club OR a Department, but not both.
/// </summary>
public class ContactAssignment
{
    /// <summary>
    /// Unique identifier for this assignment.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// ID of the user assigned as contact person.
    /// </summary>
    [Required]
    public Guid UserId { get; set; }

    /// <summary>
    /// Navigation property to the assigned user.
    /// </summary>
    public virtual User User { get; set; } = null!;

    /// <summary>
    /// ID of the club this assignment belongs to (null if assignment is for a department).
    /// </summary>
    public Guid? ClubId { get; set; }

    /// <summary>
    /// Navigation property to the club.
    /// </summary>
    public virtual Club? Club { get; set; }

    /// <summary>
    /// ID of the department this assignment belongs to (null if assignment is for a club).
    /// </summary>
    public Guid? DepartmentId { get; set; }

    /// <summary>
    /// Navigation property to the department.
    /// </summary>
    public virtual Department? Department { get; set; }

    /// <summary>
    /// Optional position/role of this contact person (e.g., "Vorstand", "Trainer").
    /// </summary>
    public Guid? ContactPositionId { get; set; }

    /// <summary>
    /// Navigation property to the contact position.
    /// </summary>
    public virtual ContactPosition? ContactPosition { get; set; }

    /// <summary>
    /// Whether this contact person can edit the assigned club or department.
    /// When true, the user has edit permissions for the club/department (and its departments if club-level).
    /// Defaults to false.
    /// </summary>
    public bool CanEdit { get; set; } = false;

    /// <summary>
    /// Optional context-specific email address for this assignment.
    /// Only used for communication after successful verification.
    /// </summary>
    [EmailAddress]
    [MaxLength(255)]
    public string? ContextEmail { get; set; }

    /// <summary>
    /// Normalized form of ContextEmail for lookup/deduplication.
    /// </summary>
    [MaxLength(255)]
    public string? ContextEmailNormalized { get; set; }

    /// <summary>
    /// Whether ContextEmail has been verified by the mailbox owner.
    /// </summary>
    public bool ContextEmailVerified { get; set; } = false;

    /// <summary>
    /// Timestamp when ContextEmail was verified (UTC).
    /// </summary>
    public DateTime? ContextEmailVerifiedAt { get; set; }

    /// <summary>
    /// Visibility level for the context email in API/UI.
    /// Does not affect backend recipient resolution after verification.
    /// </summary>
    public VisibilityLevel ContextEmailVisibility { get; set; } = VisibilityLevel.AllUsers;

    /// <summary>
    /// When this assignment was created (UTC).
    /// </summary>
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optional notes about this assignment.
    /// </summary>
    [MaxLength(500)]
    public string? Notes { get; set; }
}
