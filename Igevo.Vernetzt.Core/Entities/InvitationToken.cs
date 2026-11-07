using System.ComponentModel.DataAnnotations;
using Igevo.Vernetzt.Core.Enums;

namespace Igevo.Vernetzt.Core.Entities;

/// <summary>
/// Represents an invitation token for inviting new users to the system.
/// Tokens expire after a configurable number of days and can be revoked.
/// </summary>
public class InvitationToken
{
    public Guid Id { get; set; }

    /// <summary>
    /// Email address the invitation was sent to.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Secure token string used in the invitation URL.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// User ID of the person who sent the invitation.
    /// </summary>
    public Guid InvitedById { get; set; }

    /// <summary>
    /// When the invitation was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the invitation token expires.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Whether the invitation has been used (accepted).
    /// </summary>
    public bool IsUsed { get; set; }

    /// <summary>
    /// When the invitation was accepted.
    /// </summary>
    public DateTime? UsedAt { get; set; }

    /// <summary>
    /// Whether the invitation has been revoked.
    /// </summary>
    public bool IsRevoked { get; set; }

    /// <summary>
    /// When the invitation was revoked.
    /// </summary>
    public DateTime? RevokedAt { get; set; }

    // --- Pre-linked contact assignment (optional) ---

    /// <summary>
    /// Optional club to assign the invited user to upon acceptance.
    /// Mutually exclusive with DepartmentId.
    /// </summary>
    public Guid? ClubId { get; set; }

    /// <summary>
    /// Navigation property to the club.
    /// </summary>
    public virtual Club? Club { get; set; }

    /// <summary>
    /// Optional department to assign the invited user to upon acceptance.
    /// Mutually exclusive with ClubId.
    /// </summary>
    public Guid? DepartmentId { get; set; }

    /// <summary>
    /// Navigation property to the department.
    /// </summary>
    public virtual Department? Department { get; set; }

    /// <summary>
    /// Optional contact position for the pre-linked assignment.
    /// </summary>
    public Guid? ContactPositionId { get; set; }

    /// <summary>
    /// Navigation property to the contact position.
    /// </summary>
    public virtual ContactPosition? ContactPosition { get; set; }

    /// <summary>
    /// Whether the invited user should receive edit rights for the pre-linked club or department upon acceptance.
    /// </summary>
    public bool CanEdit { get; set; } = false;

    /// <summary>
    /// Optional details for the pre-linked assignment (e.g. "1. Mannschaft"), copied to the
    /// contact assignment's notes when the invitation is accepted.
    /// </summary>
    [MaxLength(500)]
    public string? AssignmentDetails { get; set; }

    /// <summary>
    /// Optional role/office address (e.g. vorstand@verein.de) for the pre-linked assignment.
    /// Becomes the assignment's context email on registration and must then be verified by the mailbox owner.
    /// The login always uses the personal <see cref="Email"/>.
    /// </summary>
    [EmailAddress]
    [MaxLength(255)]
    public string? ContextEmail { get; set; }

    /// <summary>
    /// Optional claims to assign to the user upon acceptance (e.g., SystemAdmin, Manager).
    /// </summary>
    public List<UserClaim> PendingClaims { get; set; } = new();
}
