using Igevo.Vernetzt.Core.Enums;

namespace Igevo.Vernetzt.Core.DTOs.Users;

/// <summary>
/// Assignment-based recipient candidate for email flows.
/// A single user can appear multiple times with different assignments/context emails.
/// </summary>
public class RecipientCandidateDto
{
    public Guid AssignmentId { get; set; }
    public bool IsAssignmentRecipient { get; set; } = true;
    public Guid UserId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}";

    public string? DisplayEmail { get; set; }
    public bool IsContextEmailUsed { get; set; }
    public bool HasContextEmail { get; set; }
    public bool ContextEmailVerified { get; set; }

    public string EmailBadgeType { get; set; } = string.Empty;
    public string EmailBadgeLabel { get; set; } = string.Empty;
    public string EmailBadgeReason { get; set; } = string.Empty;

    public Guid OrganizationId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public OrganizationType OrganizationType { get; set; }
    public Guid? ParentClubId { get; set; }
    public string? ParentClubName { get; set; }

    /// <summary>Parent department if the organization is a group.</summary>
    public Guid? ParentDepartmentId { get; set; }
    public string? ParentDepartmentName { get; set; }
    public Guid? ClubTypeId { get; set; }
    public Guid? PositionId { get; set; }
    public string? PositionName { get; set; }
    public string? AssignmentDetails { get; set; }
    public DateTime AssignedAt { get; set; }
    public bool HasAvatar { get; set; }
}

public class MyContactAssignmentDto
{
    public Guid AssignmentId { get; set; }
    public Guid OrganizationId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public OrganizationType OrganizationType { get; set; }
    public Guid? ParentClubId { get; set; }
    public string? ParentClubName { get; set; }

    /// <summary>Parent department if the organization is a group.</summary>
    public Guid? ParentDepartmentId { get; set; }
    public string? ParentDepartmentName { get; set; }
    public Guid? PositionId { get; set; }
    public string? PositionName { get; set; }
    public string? AssignmentDetails { get; set; }
    public DateTime AssignedAt { get; set; }

    public string? ContextEmail { get; set; }
    public bool ContextEmailVerified { get; set; }
    public DateTime? ContextEmailVerifiedAt { get; set; }

    /// <summary>
    /// Who may see this assignment and its role/office address.
    /// </summary>
    public VisibilityLevel Visibility { get; set; }

    /// <summary>
    /// False while the user has not yet consented to the assignment.
    /// </summary>
    public bool IsConfirmed { get; set; }
    public DateTime? ConfirmedAt { get; set; }

    public string EffectiveEmailBadgeType { get; set; } = string.Empty;
    public string EffectiveEmailBadgeLabel { get; set; } = string.Empty;
    public string EffectiveEmailBadgeReason { get; set; } = string.Empty;
}

public class UpdateMyContactAssignmentEmailDto
{
    public string? ContextEmail { get; set; }

    /// <summary>
    /// New visibility of the assignment and its role/office address. Only the assigned user may change it,
    /// and only for a confirmed assignment (a pending one gets its visibility on consent). Null keeps it.
    /// </summary>
    public VisibilityLevel? Visibility { get; set; }
}

/// <summary>
/// Consent of the assigned user to a pending contact assignment.
/// </summary>
public class ConfirmContactAssignmentDto
{
    /// <summary>
    /// Who may see the assignment and its role/office address.
    /// </summary>
    public VisibilityLevel Visibility { get; set; } = VisibilityLevel.Public;
}

public class UpdateMyContactAssignmentEmailResponse
{
    public bool Success { get; set; }
    public bool VerificationEmailSent { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class ContactAssignmentEmailVerificationResultDto
{
    public bool Success { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
