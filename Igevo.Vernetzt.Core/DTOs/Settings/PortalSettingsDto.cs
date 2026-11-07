using System.ComponentModel.DataAnnotations;

namespace Igevo.Vernetzt.Core.DTOs.Settings;

/// <summary>
/// Texts and branding of the public portal (SystemAdmin settings).
/// </summary>
public sealed class PortalSettingsDto
{
    /// <summary>Heading of the start page. Empty = default heading.</summary>
    [MaxLength(120)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Intro text below the heading. Empty = default text.</summary>
    [MaxLength(300)]
    public string Subtitle { get; set; } = string.Empty;

    [Required]
    public PortalLegalPageDto Imprint { get; set; } = new();

    [Required]
    public PortalLegalPageDto Privacy { get; set; } = new();

    /// <summary>Optional name of the organisation offering the portal (umbrella club, municipality).</summary>
    [MaxLength(200)]
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>Optional website of the provider.</summary>
    [MaxLength(2000)]
    public string ProviderUrl { get; set; } = string.Empty;

    /// <summary>Optional notice shown on every portal page below the header. Empty = no banner.</summary>
    [MaxLength(500)]
    public string NoticeText { get; set; } = string.Empty;

    /// <summary>Severity of the notice: "info" or "warning".</summary>
    [Required]
    [RegularExpression("^(info|warning)$")]
    public string NoticeLevel { get; set; } = "info";

    /// <summary>Whether a logo has been uploaded (read-only, managed via the logo endpoints).</summary>
    public bool HasLogo { get; set; }
}

/// <summary>
/// Imprint or privacy page: own rich text or a link to an external page. Both values are kept so
/// switching the mode does not lose the other one.
/// </summary>
public sealed class PortalLegalPageDto
{
    public const string ModeText = "text";
    public const string ModeLink = "link";

    [Required]
    [RegularExpression("^(text|link)$")]
    public string Mode { get; set; } = ModeText;

    [MaxLength(100_000)]
    public string Html { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Url { get; set; } = string.Empty;
}
