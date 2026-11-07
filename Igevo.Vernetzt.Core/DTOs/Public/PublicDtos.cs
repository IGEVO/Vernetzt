using Igevo.Vernetzt.Core.Enums;

namespace Igevo.Vernetzt.Core.DTOs.Public;

public sealed record PublicPagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}

public sealed record PublicClubTypeDto(Guid Id, string Name, string? Description);

public sealed record PublicSocialMediaLinkDto(SocialMediaPlatform Platform, string Url);

/// <summary>
/// Public contact person. Email address and phone number are deliberately not part of this
/// record; they can be revealed one at a time via the rate limited contact-details endpoint.
/// </summary>
public sealed record PublicContactDto(
    Guid Id,
    string FirstName,
    string LastName,
    string? PositionName,
    bool HasEmail,
    bool HasPhoneNumber);

/// <summary>Contact data revealed on request. Fields that are not public are null.</summary>
public sealed record PublicContactDetailsDto(string? Email, string? PhoneNumber);

/// <param name="Description">Public info text (e.g. training times), plain text.</param>
/// <param name="GroupCount">Number of groups below a department (0 for groups).</param>
public sealed record PublicDepartmentSummaryDto(
    Guid Id,
    string Name,
    string? City,
    string? WebsiteUrl,
    string? LogoUrl,
    string? Description,
    int GroupCount);

/// <summary>Reference to a parent organization, used for breadcrumbs.</summary>
public sealed record PublicOrganizationReferenceDto(Guid Id, string Name);

/// <summary>
/// Origin of a value a department or group took over from a higher level because its own was empty.
/// </summary>
/// <param name="Kind">"club" or "department".</param>
public sealed record PublicInheritedSourceDto(string Kind, Guid Id, string Name)
{
    public const string ClubKind = "club";
    public const string DepartmentKind = "department";
}

/// <summary>
/// A department or – if <paramref name="ParentDepartment"/> is set – a group below a department.
/// Empty address, email, website and social media blocks are filled from the parent department or
/// the club (group &gt; department &gt; club); the matching <c>*Source</c> names where a value came
/// from and is null for the entity's own values.
/// </summary>
public sealed record PublicDepartmentDto(
    Guid Id,
    string Name,
    Guid ClubId,
    string ClubName,
    PublicOrganizationReferenceDto? ParentDepartment,
    string? Description,
    string? Street,
    string? PostalCode,
    string? City,
    string? WebsiteUrl,
    IReadOnlyList<PublicSocialMediaLinkDto> SocialMediaLinks,
    bool HasEmail,
    string? LogoUrl,
    IReadOnlyList<PublicContactDto> Contacts,
    IReadOnlyList<PublicDepartmentSummaryDto> Groups,
    DateTime UpdatedAt,
    PublicInheritedSourceDto? AddressSource,
    PublicInheritedSourceDto? EmailSource,
    PublicInheritedSourceDto? WebsiteSource,
    PublicInheritedSourceDto? SocialMediaSource);

public sealed record PublicClubSummaryDto(
    Guid Id,
    string Name,
    Guid? ClubTypeId,
    string? ClubTypeName,
    string? City,
    string? WebsiteUrl,
    string? LogoUrl,
    int DepartmentCount);

public sealed record PublicClubDto(
    Guid Id,
    string Name,
    string? Description,
    Guid? ClubTypeId,
    string? ClubTypeName,
    string? Street,
    string? PostalCode,
    string? City,
    string? WebsiteUrl,
    IReadOnlyList<PublicSocialMediaLinkDto> SocialMediaLinks,
    bool HasEmail,
    string? LogoUrl,
    IReadOnlyList<PublicContactDto> Contacts,
    IReadOnlyList<PublicDepartmentSummaryDto> Departments,
    DateTime UpdatedAt);

public sealed record PublicEventImageVariantDto(int Width, string Url);

public sealed record PublicEventImageDto(
    Guid Id,
    EventMediaType Type,
    string ContentType,
    double FocusX,
    double FocusY,
    int? Width,
    int? Height,
    string FileUrl,
    IReadOnlyList<PublicEventImageVariantDto> Variants);

public sealed record PublicEventDto(
    Guid Id,
    string Title,
    string? Description,
    string? Location,
    DateTime StartUtc,
    DateTime EndUtc,
    bool IsAllDay,
    bool IsHighlight,
    Guid? ClubId,
    Guid? DepartmentId,
    string? ClubName,
    string? DepartmentName,
    string? ParentDepartmentName,
    PublicEventImageDto? Image,
    PublicEventImageDto? CardImage,
    PublicEventImageDto? HeaderImage,
    IReadOnlyList<PublicEventImageDto> Media,
    DateTime UpdatedAt);

/// <summary>Imprint or privacy page of the public portal: own rich text or an external link.</summary>
/// <param name="Mode">"text" or "link".</param>
/// <param name="Html">Sanitized HTML (mode "text"); null if not yet maintained.</param>
/// <param name="Url">External URL (mode "link").</param>
public sealed record PublicPortalLegalPageDto(string Mode, string? Html, string? Url);

public sealed record PublicPortalProviderDto(string Name, string? Url);

/// <summary>Banner shown on every portal page.</summary>
/// <param name="Level">"info" or "warning".</param>
/// <param name="Text">Plain text.</param>
public sealed record PublicPortalNoticeDto(string Level, string Text);

/// <summary>Texts and branding of the public portal, maintained by system admins.</summary>
public sealed record PublicPortalDto(
    string? Title,
    string? Subtitle,
    PublicPortalLegalPageDto Imprint,
    PublicPortalLegalPageDto Privacy,
    PublicPortalProviderDto? Provider,
    string? LogoUrl,
    PublicPortalNoticeDto? Notice);
