using Igevo.Vernetzt.Core.Enums;

namespace Igevo.Vernetzt.Core.DTOs.Admin;

/// <summary>
/// Overview of everything that still waits for a reaction of the invited or assigned person
/// (administration only).
/// </summary>
public class PendingItemsOverviewDto
{
    /// <summary>Invitations that are neither accepted, revoked nor expired.</summary>
    public List<PendingInvitationDto> Invitations { get; set; } = [];

    /// <summary>Contact assignments the assigned person has not consented to yet.</summary>
    public List<PendingAssignmentConsentDto> Assignments { get; set; } = [];

    /// <summary>Role/office addresses whose mailbox owner has not confirmed them yet.</summary>
    public List<PendingContextEmailDto> ContextEmails { get; set; } = [];
}

/// <summary>
/// Organization path of a pending item (club, or club › department, or club › department › group).
/// </summary>
public abstract class PendingOrganizationItemDto
{
    public Guid? ClubId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string? ClubName { get; set; }
    public string? ParentDepartmentName { get; set; }
    public string? DepartmentName { get; set; }
    public string? PositionName { get; set; }
}

/// <summary>
/// An open invitation.
/// </summary>
public class PendingInvitationDto : PendingOrganizationItemDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? InvitedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    /// <summary>Role/office address of the pre-linked assignment, if any.</summary>
    public string? ContextEmail { get; set; }

    public List<UserClaim> PendingClaims { get; set; } = [];
}

/// <summary>
/// A contact assignment awaiting consent of the assigned person.
/// </summary>
public class PendingAssignmentConsentDto : PendingOrganizationItemDto
{
    public Guid AssignmentId { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string? AssignedByName { get; set; }
    public DateTime AssignedAt { get; set; }

    /// <summary>When the consent request was last sent; reminders are limited to one per 24 hours.</summary>
    public DateTime? LastConsentRequestAt { get; set; }
}

/// <summary>
/// A role/office address awaiting confirmation by its mailbox owner.
/// </summary>
public class PendingContextEmailDto : PendingOrganizationItemDto
{
    public Guid AssignmentId { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string ContextEmail { get; set; } = string.Empty;

    /// <summary>When the latest confirmation mail was sent, if any.</summary>
    public DateTime? LastVerificationSentAt { get; set; }

    /// <summary>Whether the assigned person has already consented to the assignment itself.</summary>
    public bool IsAssignmentConfirmed { get; set; }
}
