namespace Igevo.Vernetzt.Core.DTOs.Settings;

/// <summary>
/// Privacy policy or imprint of Vernetzt as shown to everyone (also without sign-in): either an own text,
/// published on the page <c>/datenschutz</c> or <c>/impressum</c> of the app, or a link to an external page.
/// </summary>
public sealed class PublicPrivacyPolicyDto
{
    /// <summary>"text" (own text, see <see cref="Html"/>) or "link" (external page, see <see cref="Url"/>).</summary>
    public string Mode { get; set; } = "link";

    /// <summary>Sanitized rich text (only in text mode).</summary>
    public string? Html { get; set; }

    /// <summary>Address of the external page (only in link mode).</summary>
    public string? Url { get; set; }
}
