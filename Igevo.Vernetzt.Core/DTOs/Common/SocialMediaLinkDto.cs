using System.ComponentModel.DataAnnotations;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;

namespace Igevo.Vernetzt.Core.DTOs.Common;

/// <summary>
/// Social media link of a club or department. Publicly visible like the website.
/// </summary>
public class SocialMediaLinkDto
{
    public SocialMediaPlatform Platform { get; set; }

    [Required(ErrorMessage = "URL ist erforderlich")]
    [StringLength(SocialMediaLink.MaxUrlLength, ErrorMessage = "Social-Media-URL darf maximal 500 Zeichen lang sein")]
    public string Url { get; set; } = string.Empty;

    public static SocialMediaLinkDto FromEntity(SocialMediaLink link) => new() { Platform = link.Platform, Url = link.Url };

    public static List<SocialMediaLinkDto> FromEntities(IEnumerable<SocialMediaLink>? links) =>
        (links ?? []).Select(FromEntity).ToList();

    public static List<SocialMediaLink> ToEntities(IEnumerable<SocialMediaLinkDto>? links) =>
        SocialMediaLink.Normalize((links ?? []).OfType<SocialMediaLinkDto>().Select(l => new SocialMediaLink(l.Platform, l.Url)));
}
