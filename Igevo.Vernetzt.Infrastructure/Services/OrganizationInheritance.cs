using Igevo.Vernetzt.Core.DTOs.Public;
using Igevo.Vernetzt.Core.Entities;

namespace Igevo.Vernetzt.Infrastructure.Services;

/// <summary>
/// Values of one level in the inheritance chain group &gt; department &gt; club; <see cref="Source"/> is null
/// for the entity itself. Empty blocks (address, email, website, social media) are taken from the first
/// level further up that has them filled.
/// </summary>
internal sealed record OrganizationInheritanceLevel(
    PublicInheritedSourceDto? Source,
    string? Street,
    string? PostalCode,
    string? City,
    string? Email,
    string? WebsiteUrl,
    IReadOnlyList<SocialMediaLink> SocialMediaLinks)
{
    /// <summary>The address is inherited as a whole, so street and city never come from different levels.</summary>
    public bool HasAddress => !string.IsNullOrWhiteSpace(Street) ||
                              !string.IsNullOrWhiteSpace(PostalCode) ||
                              !string.IsNullOrWhiteSpace(City);

    public static OrganizationInheritanceLevel Own(Department d) => FromDepartment(d) with { Source = null };

    public static OrganizationInheritanceLevel Own(Club c) => FromClub(c) with { Source = null };

    public static OrganizationInheritanceLevel FromDepartment(Department d) => new(
        new PublicInheritedSourceDto(PublicInheritedSourceDto.DepartmentKind, d.Id, d.Name),
        d.Street, d.PostalCode, d.City, d.Email, d.WebsiteUrl, d.SocialMediaLinks ?? []);

    public static OrganizationInheritanceLevel FromClub(Club c) => new(
        new PublicInheritedSourceDto(PublicInheritedSourceDto.ClubKind, c.Id, c.Name),
        c.Street, c.PostalCode, c.City, c.Email, c.WebsiteUrl, c.SocialMediaLinks ?? []);

    /// <summary>
    /// The chain for a department or group: the entity itself, its parent department (groups only), the club.
    /// </summary>
    public static IReadOnlyList<OrganizationInheritanceLevel> Chain(Department department, Department? parent, Club club)
    {
        var chain = new List<OrganizationInheritanceLevel> { Own(department) };
        if (parent is not null) chain.Add(FromDepartment(parent));
        chain.Add(FromClub(club));
        return chain;
    }
}

/// <summary>
/// The effective values of an inheritance chain, each block with the level it came from.
/// </summary>
internal sealed record OrganizationInheritanceResult(
    OrganizationInheritanceLevel? Address,
    OrganizationInheritanceLevel? Email,
    OrganizationInheritanceLevel? Website,
    OrganizationInheritanceLevel? SocialMedia)
{
    public static OrganizationInheritanceResult Resolve(IReadOnlyList<OrganizationInheritanceLevel> chain) => new(
        chain.FirstOrDefault(l => l.HasAddress),
        chain.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l.Email)),
        chain.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l.WebsiteUrl)),
        chain.FirstOrDefault(l => l.SocialMediaLinks.Count > 0));
}
