using System.ComponentModel.DataAnnotations;
using Igevo.Vernetzt.Core.DTOs.Common;
using Igevo.Vernetzt.Core.DTOs.ContactPositions;
using Igevo.Vernetzt.Core.DTOs.Locations;
using Igevo.Vernetzt.Core.Enums;

namespace Igevo.Vernetzt.Core.DTOs.Departments;

public class DepartmentDto
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Name ist erforderlich")]
    [StringLength(200, ErrorMessage = "Name darf maximal 200 Zeichen lang sein")]
    public string Name { get; set; } = string.Empty;

    [StringLength(20, ErrorMessage = "Abkürzung darf maximal 20 Zeichen lang sein")]
    public string? ShortName { get; set; }

    public Guid ClubId { get; set; }
    public string? ClubName { get; set; }

    [StringLength(4000, ErrorMessage = "Beschreibung darf maximal 4000 Zeichen lang sein")]
    public string? Description { get; set; }

    /// <summary>Parent department if this entry is a group; null for departments.</summary>
    public Guid? ParentDepartmentId { get; set; }
    public string? ParentDepartmentName { get; set; }

    /// <summary>True if this entry is a group below a department.</summary>
    public bool IsGroup { get; set; }

    /// <summary>Groups below this department (always empty for groups).</summary>
    public List<DepartmentSummaryDto> Groups { get; set; } = new();

    [StringLength(200, ErrorMessage = "Straße darf maximal 200 Zeichen lang sein")]
    public string? Street { get; set; }

    [StringLength(10, ErrorMessage = "Postleitzahl darf maximal 10 Zeichen lang sein")]
    public string? PostalCode { get; set; }

    [StringLength(100, ErrorMessage = "Stadt darf maximal 100 Zeichen lang sein")]
    public string? City { get; set; }

    [Url(ErrorMessage = "Ungültige URL")]
    [StringLength(500, ErrorMessage = "Website-URL darf maximal 500 Zeichen lang sein")]
    public string? Website { get; set; }

    [EmailAddress(ErrorMessage = "Ungültige E-Mail-Adresse")]
    [StringLength(200, ErrorMessage = "E-Mail darf maximal 200 Zeichen lang sein")]
    public string? Email { get; set; }

    public List<SocialMediaLinkDto>? SocialMediaLinks { get; set; } = new();

    /// <summary>Locations used by this organization (not inherited).</summary>
    public List<LocationSummaryDto> Locations { get; set; } = new();

    public List<ContactAssignmentDto> ContactAssignments { get; set; } = new();

    // Permissions for current user
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
    public bool CanManageContacts { get; set; }

    /// <summary>
    /// Indicates whether the department has an avatar image.
    /// </summary>
    public bool HasAvatar { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class DepartmentSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? ParentDepartmentId { get; set; }
    public int GroupCount { get; set; }
    public string? City { get; set; }
    public string? Email { get; set; }
    public int ContactPersonsCount { get; set; }
    public bool CanEdit { get; set; }

    /// <summary>
    /// Indicates whether the department has an avatar image.
    /// </summary>
    public bool HasAvatar { get; set; }
}

public class CreateDepartmentDto
{
    [Required(ErrorMessage = "Name ist erforderlich")]
    [StringLength(200, ErrorMessage = "Name darf maximal 200 Zeichen lang sein")]
    public string Name { get; set; } = string.Empty;

    [StringLength(20, ErrorMessage = "Abkürzung darf maximal 20 Zeichen lang sein")]
    public string? ShortName { get; set; }

    [Required(ErrorMessage = "Verein ist erforderlich")]
    public Guid ClubId { get; set; }

    [StringLength(4000, ErrorMessage = "Beschreibung darf maximal 4000 Zeichen lang sein")]
    public string? Description { get; set; }

    /// <summary>
    /// Set to create a group below this department. Must be a top-level department of the same club.
    /// </summary>
    public Guid? ParentDepartmentId { get; set; }

    [StringLength(200, ErrorMessage = "Straße darf maximal 200 Zeichen lang sein")]
    public string? Street { get; set; }

    [StringLength(10, ErrorMessage = "Postleitzahl darf maximal 10 Zeichen lang sein")]
    public string? PostalCode { get; set; }

    [StringLength(100, ErrorMessage = "Stadt darf maximal 100 Zeichen lang sein")]
    public string? City { get; set; }

    [Url(ErrorMessage = "Ungültige URL")]
    [StringLength(500, ErrorMessage = "Website-URL darf maximal 500 Zeichen lang sein")]
    public string? Website { get; set; }

    [EmailAddress(ErrorMessage = "Ungültige E-Mail-Adresse")]
    [StringLength(200, ErrorMessage = "E-Mail darf maximal 200 Zeichen lang sein")]
    public string? Email { get; set; }

    public List<SocialMediaLinkDto>? SocialMediaLinks { get; set; } = new();

    /// <summary>Ids of the locations used by this organization; null leaves them unchanged.</summary>
    public List<Guid>? LocationIds { get; set; }
}

public class UpdateDepartmentDto
{
    [Required(ErrorMessage = "Name ist erforderlich")]
    [StringLength(200, ErrorMessage = "Name darf maximal 200 Zeichen lang sein")]
    public string Name { get; set; } = string.Empty;

    [StringLength(20, ErrorMessage = "Abkürzung darf maximal 20 Zeichen lang sein")]
    public string? ShortName { get; set; }

    [StringLength(4000, ErrorMessage = "Beschreibung darf maximal 4000 Zeichen lang sein")]
    public string? Description { get; set; }

    [StringLength(200, ErrorMessage = "Straße darf maximal 200 Zeichen lang sein")]
    public string? Street { get; set; }

    [StringLength(10, ErrorMessage = "Postleitzahl darf maximal 10 Zeichen lang sein")]
    public string? PostalCode { get; set; }

    [StringLength(100, ErrorMessage = "Stadt darf maximal 100 Zeichen lang sein")]
    public string? City { get; set; }

    [Url(ErrorMessage = "Ungültige URL")]
    [StringLength(500, ErrorMessage = "Website-URL darf maximal 500 Zeichen lang sein")]
    public string? Website { get; set; }

    [EmailAddress(ErrorMessage = "Ungültige E-Mail-Adresse")]
    [StringLength(200, ErrorMessage = "E-Mail darf maximal 200 Zeichen lang sein")]
    public string? Email { get; set; }

    public List<SocialMediaLinkDto>? SocialMediaLinks { get; set; } = new();

    /// <summary>Ids of the locations used by this organization; null leaves them unchanged.</summary>
    public List<Guid>? LocationIds { get; set; }
}

public class UpdateDepartmentContactsDto
{
    [Required]
    public List<ContactAssignmentInputDto> ContactAssignments { get; set; } = new();
}

public class ContactAssignmentInputDto
{
    [Required]
    public Guid UserId { get; set; }

    public Guid? ContactPositionId { get; set; }
}

