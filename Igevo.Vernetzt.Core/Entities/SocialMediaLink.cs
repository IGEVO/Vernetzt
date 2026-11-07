using Igevo.Vernetzt.Core.Enums;

namespace Igevo.Vernetzt.Core.Entities;

/// <summary>
/// Link to a social media profile of a club or department. Stored as part of the owning entity (JSON column).
/// </summary>
public sealed record SocialMediaLink(SocialMediaPlatform Platform, string Url)
{
    /// <summary>Maximum number of links per club or department.</summary>
    public const int MaxLinksPerOwner = 10;

    /// <summary>Maximum length of a link URL.</summary>
    public const int MaxUrlLength = 500;

    /// <summary>
    /// Trims URLs, drops empty entries and duplicates (same URL, case-insensitive) while keeping the given order.
    /// </summary>
    public static List<SocialMediaLink> Normalize(IEnumerable<SocialMediaLink>? links) =>
        (links ?? [])
            .Select(l => l with { Url = l.Url?.Trim() ?? string.Empty })
            .Where(l => l.Url.Length > 0)
            .DistinctBy(l => l.Url, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
