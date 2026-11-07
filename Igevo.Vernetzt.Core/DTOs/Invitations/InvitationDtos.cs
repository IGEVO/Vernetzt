using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Igevo.Vernetzt.Core.Enums;

namespace Igevo.Vernetzt.Core.DTOs.Invitations;

/// <summary>
/// DTO for inviting a new user, optionally with a pre-linked contact assignment.
/// </summary>
public class InviteUserDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Optional club to assign the invited user to upon acceptance.
    /// Mutually exclusive with DepartmentId.
    /// </summary>
    public Guid? ClubId { get; set; }

    /// <summary>
    /// Optional department to assign the invited user to upon acceptance.
    /// Mutually exclusive with ClubId.
    /// </summary>
    public Guid? DepartmentId { get; set; }

    /// <summary>
    /// Optional contact position for the pre-linked assignment.
    /// </summary>
    public Guid? ContactPositionId { get; set; }

    /// <summary>
    /// Whether the invited user should have edit rights for the pre-linked club or department.
    /// </summary>
    public bool CanEdit { get; set; }

    /// <summary>
    /// Optional details for the pre-linked assignment (e.g. "1. Mannschaft, U17").
    /// </summary>
    [StringLength(500)]
    public string? AssignmentDetails { get; set; }

    /// <summary>
    /// Optional role/office address (e.g. vorstand@verein.de) for the pre-linked assignment.
    /// Stored as context email of the assignment on registration (verification required);
    /// <see cref="Email"/> stays the personal login address.
    /// </summary>
    [EmailAddress]
    [StringLength(255)]
    public string? ContextEmail { get; set; }

    /// <summary>
    /// Optional claims to pre-assign to the user upon acceptance.
    /// </summary>
    public List<UserClaim>? PendingClaims { get; set; }
}

/// <summary>
/// DTO for accepting an invitation and completing onboarding.
/// </summary>
public class AcceptInvitationDto
{
    [Required]
    public string Token { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Phone]
    [StringLength(50)]
    public string? PhoneNumber { get; set; }

    public VisibilityLevel EmailVisibility { get; set; } = VisibilityLevel.AllUsers;

    public VisibilityLevel PhoneVisibility { get; set; } = VisibilityLevel.Internal;

    [Required]
    public bool AcceptedPrivacyPolicy { get; set; }

    /// <summary>
    /// How the user wants to sign in in the future (email link or passkey).
    /// </summary>
    public LoginMethod PreferredLoginMethod { get; set; } = LoginMethod.EmailLink;

    /// <summary>
    /// Optional role/office addresses per pending assignment, keyed by <see cref="PendingAssignmentDto.InvitationId"/>.
    /// A value overrides the address suggested by the inviter; an empty value removes it.
    /// Each address must be verified by the mailbox owner before it is used.
    /// </summary>
    public Dictionary<Guid, string?>? AssignmentContextEmails { get; set; }
}

/// <summary>
/// Response after sending an invitation.
/// </summary>
public class InvitationResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool IsExistingUser { get; set; }
}

/// <summary>
/// Response after accepting an invitation.
/// </summary>
public class AcceptInvitationResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? LoginUrl { get; set; }
    public bool MagicLinkSent { get; set; }
    public string? Email { get; set; }
}

/// <summary>
/// Response with invitation details for the onboarding page.
/// </summary>
public class InvitationDetailsResponse
{
    public string Email { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public string PrivacyPolicyUrl { get; set; } = string.Empty;

    /// <summary>
    /// All pending contact assignments that will be created upon acceptance.
    /// Aggregated from all valid invitations for this email.
    /// </summary>
    public List<PendingAssignmentDto> PendingAssignments { get; set; } = new();

    /// <summary>
    /// Roles that will be granted upon acceptance (aggregated from all valid invitations).
    /// Only used server-side to derive <see cref="PasskeyRequired"/>.
    /// </summary>
    [JsonIgnore]
    public List<UserClaim> PendingClaims { get; set; } = new();

    /// <summary>
    /// The pending roles require a passkey, so signing in via email link is not offered.
    /// </summary>
    public bool PasskeyRequired { get; set; }
}

/// <summary>
/// Represents a pending contact assignment from an invitation.
/// </summary>
public class PendingAssignmentDto
{
    /// <summary>
    /// The invitation that carries this assignment; key for <see cref="AcceptInvitationDto.AssignmentContextEmails"/>.
    /// </summary>
    public Guid InvitationId { get; set; }

    public string? ClubName { get; set; }
    public string? DepartmentName { get; set; }

    /// <summary>Parent department if the invitation assigns to a group.</summary>
    public string? ParentDepartmentName { get; set; }

    public string? PositionName { get; set; }

    /// <summary>
    /// Role/office address suggested by the inviter for this assignment, if any.
    /// </summary>
    public string? ContextEmail { get; set; }
}
