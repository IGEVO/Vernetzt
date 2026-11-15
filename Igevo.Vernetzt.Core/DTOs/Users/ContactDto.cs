using Igevo.Vernetzt.Core.Enums;

namespace Igevo.Vernetzt.Core.DTOs.Users;

/// <summary>
/// Extended user DTO for contact directory with organization assignments.
/// </summary>
public class ContactDto
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}";

    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }

    public VisibilityLevel EmailVisibility { get; set; }
    public VisibilityLevel PhoneVisibility { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Indicates whether the user has an avatar image.
    /// </summary>
    public bool HasAvatar { get; set; }

    public List<ContactOrganizationDto> Organizations { get; set; } = new();
}

/// <summary>
/// Represents a club, department or group assignment for a contact. Groups use type Department
/// and carry their parent department.
/// </summary>
public class ContactOrganizationDto
{
    public Guid OrganizationId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    /// <summary>Optional abbreviation of the organization.</summary>
    public string? OrganizationShortName { get; set; }
    public OrganizationType Type { get; set; }
    /// <summary>Parent club (for departments and groups).</summary>
    public Guid? ParentId { get; set; }
    public string? ParentName { get; set; }
    public string? ParentShortName { get; set; }

    /// <summary>Parent department if the organization is a group.</summary>
    public Guid? ParentDepartmentId { get; set; }
    public string? ParentDepartmentName { get; set; }
    public string? ParentDepartmentShortName { get; set; }
    public Guid? ClubTypeId { get; set; }
    public Guid? PositionId { get; set; }
    public string? PositionName { get; set; }
    public string? AssignmentEmail { get; set; }
    public DateTime AssignedAt { get; set; }

    /// <summary>
    /// False while the user has not yet consented (only shown to the user, administration and editors).
    /// </summary>
    public bool IsConfirmed { get; set; } = true;
}

public enum OrganizationType
{
    Club,
    Department,
    None
}
