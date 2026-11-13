using Igevo.Vernetzt.Core.Enums;

namespace Igevo.Vernetzt.Core.DTOs.ContactPositions;

/// <summary>
/// DTO representing a contact assignment with user details and position.
/// </summary>
public class ContactAssignmentDto
{
    public Guid AssignmentId { get; set; }
    public Guid UserId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public Guid? ContactPositionId { get; set; }
    public string? PositionName { get; set; }
    public bool CanEdit { get; set; }

    /// <summary>Whether this contact person receives location-specific information (e.g. hall closures).</summary>
    public bool ReceivesLocationInfo { get; set; }
    public DateTime AssignedAt { get; set; }
    public string? AssignmentDetails { get; set; }

    public string? ContextEmail { get; set; }
    public bool ContextEmailVerified { get; set; }
    public DateTime? ContextEmailVerifiedAt { get; set; }

    /// <summary>
    /// Who may see this assignment and its role/office address (chosen by the assigned user).
    /// </summary>
    public VisibilityLevel Visibility { get; set; }

    /// <summary>
    /// False while the assigned user has not yet consented (pending; no rights, only visible internally).
    /// </summary>
    public bool IsConfirmed { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public string? EmailBadgeType { get; set; }
    public string? EmailBadgeLabel { get; set; }
    public string? EmailBadgeReason { get; set; }

    /// <summary>
    /// Indicates whether the user has an avatar image.
    /// </summary>
    public bool HasAvatar { get; set; }
}
