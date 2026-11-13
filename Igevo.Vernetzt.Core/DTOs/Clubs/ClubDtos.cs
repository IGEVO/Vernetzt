using System.ComponentModel.DataAnnotations;
using Igevo.Vernetzt.Core.DTOs.Common;
using Igevo.Vernetzt.Core.DTOs.ContactPositions;
using Igevo.Vernetzt.Core.DTOs.Departments;
using Igevo.Vernetzt.Core.Enums;

namespace Igevo.Vernetzt.Core.DTOs.Clubs;

public class ClubDto
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Name ist erforderlich")]
    [StringLength(200, ErrorMessage = "Name darf maximal 200 Zeichen lang sein")]
    public string Name { get; set; } = string.Empty;

    [StringLength(20, ErrorMessage = "Abkürzung darf maximal 20 Zeichen lang sein")]
    public string? ShortName { get; set; }

    [StringLength(4000, ErrorMessage = "Beschreibung darf maximal 4000 Zeichen lang sein")]
    public string? Description { get; set; }

    public Guid? ClubTypeId { get; set; }
    public string? ClubTypeName { get; set; }

    [StringLength(200, ErrorMessage = "Straße darf maximal 200 Zeichen lang sein")]
    public string? Street { get; set; }

    [StringLength(10, ErrorMessage = "Postleitzahl darf maximal 10 Zeichen lang sein")]
    public string? PostalCode { get; set; }

    [StringLength(100, ErrorMessage = "Stadt darf maximal 100 Zeichen lang sein")]
    public string? City { get; set; }

    [Url(ErrorMessage = "Ungültige URL")]
    [StringLength(500, ErrorMessage = "Website-URL darf maximal 500 Zeichen lang sein")]
    public string? WebsiteUrl { get; set; }

    [EmailAddress(ErrorMessage = "Ungültige E-Mail-Adresse")]
    [StringLength(200, ErrorMessage = "E-Mail darf maximal 200 Zeichen lang sein")]
    public string? Email { get; set; }

    public List<SocialMediaLinkDto>? SocialMediaLinks { get; set; } = new();

    public List<ContactAssignmentDto> ContactAssignments { get; set; } = new();
    public List<DepartmentSummaryDto> Departments { get; set; } = new();

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Indicates whether the club has an avatar image.
    /// </summary>
    public bool HasAvatar { get; set; }

    // Permission indicators for the current user
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
    public bool CanManageContacts { get; set; }
}

/// <summary>
/// Summary DTO for club lists (without departments).
/// </summary>
public class ClubSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? ClubTypeId { get; set; }
    public string? ClubTypeName { get; set; }
    public string? City { get; set; }
    public string? Email { get; set; }
    public string? WebsiteUrl { get; set; }
    public bool HasAvatar { get; set; }
    public int DepartmentCount { get; set; }

    /// <summary>
    /// Departments whose name matches the current search term (empty without search),
    /// so list views can show why a club was found.
    /// </summary>
    public List<DepartmentReferenceDto> MatchingDepartments { get; set; } = new();
}

/// <summary>
/// Minimal department reference (id and name) for list views.
/// </summary>
public class DepartmentReferenceDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Name of the parent department if the match is a group.</summary>
    public string? ParentDepartmentName { get; set; }
}

public class CreateClubDto
{
    [Required(ErrorMessage = "Name ist erforderlich")]
    [StringLength(200, ErrorMessage = "Name darf maximal 200 Zeichen lang sein")]
    public string Name { get; set; } = string.Empty;

    [StringLength(20, ErrorMessage = "Abkürzung darf maximal 20 Zeichen lang sein")]
    public string? ShortName { get; set; }

    [StringLength(4000, ErrorMessage = "Beschreibung darf maximal 4000 Zeichen lang sein")]
    public string? Description { get; set; }

    public Guid? ClubTypeId { get; set; }

    [StringLength(200, ErrorMessage = "Straße darf maximal 200 Zeichen lang sein")]
    public string? Street { get; set; }

    [StringLength(10, ErrorMessage = "Postleitzahl darf maximal 10 Zeichen lang sein")]
    public string? PostalCode { get; set; }

    [StringLength(100, ErrorMessage = "Stadt darf maximal 100 Zeichen lang sein")]
    public string? City { get; set; }

    [Url(ErrorMessage = "Ungültige URL")]
    [StringLength(500, ErrorMessage = "Website-URL darf maximal 500 Zeichen lang sein")]
    public string? WebsiteUrl { get; set; }

    [EmailAddress(ErrorMessage = "Ungültige E-Mail-Adresse")]
    [StringLength(200, ErrorMessage = "E-Mail darf maximal 200 Zeichen lang sein")]
    public string? Email { get; set; }

    public List<SocialMediaLinkDto>? SocialMediaLinks { get; set; } = new();
}

public class UpdateClubDto
{
    [Required(ErrorMessage = "Name ist erforderlich")]
    [StringLength(200, ErrorMessage = "Name darf maximal 200 Zeichen lang sein")]
    public string Name { get; set; } = string.Empty;

    [StringLength(20, ErrorMessage = "Abkürzung darf maximal 20 Zeichen lang sein")]
    public string? ShortName { get; set; }

    [StringLength(4000, ErrorMessage = "Beschreibung darf maximal 4000 Zeichen lang sein")]
    public string? Description { get; set; }

    public Guid? ClubTypeId { get; set; }

    [StringLength(200, ErrorMessage = "Straße darf maximal 200 Zeichen lang sein")]
    public string? Street { get; set; }

    [StringLength(10, ErrorMessage = "Postleitzahl darf maximal 10 Zeichen lang sein")]
    public string? PostalCode { get; set; }

    [StringLength(100, ErrorMessage = "Stadt darf maximal 100 Zeichen lang sein")]
    public string? City { get; set; }

    [Url(ErrorMessage = "Ungültige URL")]
    [StringLength(500, ErrorMessage = "Website-URL darf maximal 500 Zeichen lang sein")]
    public string? WebsiteUrl { get; set; }

    [EmailAddress(ErrorMessage = "Ungültige E-Mail-Adresse")]
    [StringLength(200, ErrorMessage = "E-Mail darf maximal 200 Zeichen lang sein")]
    public string? Email { get; set; }

    public List<SocialMediaLinkDto>? SocialMediaLinks { get; set; } = new();
}

/// <summary>
/// DTO for adding/updating contact assignments to a club.
/// </summary>
public class UpdateClubContactsDto
{
    public List<ContactAssignmentInputDto> ContactAssignments { get; set; } = new();
}

/// <summary>
/// Input for creating a contact assignment.
/// </summary>
public class ContactAssignmentInputDto
{
    [Required(ErrorMessage = "Benutzer-ID ist erforderlich")]
    public Guid UserId { get; set; }

    public Guid? ContactPositionId { get; set; }
}
