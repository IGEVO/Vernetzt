using System.ComponentModel.DataAnnotations;

namespace Igevo.Vernetzt.Core.DTOs.Settings;

/// <summary>
/// DTO for reading/writing application settings.
/// </summary>
public class AppSettingsDto
{
    /// <summary>
    /// Name of the contact person shown in email footers.
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string ContactName { get; set; } = string.Empty;

    /// <summary>
    /// Email address of the contact person shown in email footers.
    /// </summary>
    [Required]
    [EmailAddress]
    [MaxLength(200)]
    public string ContactEmail { get; set; } = string.Empty;

    /// <summary>
    /// "link": the privacy policy is the external page <see cref="PrivacyPolicyUrl"/>;
    /// "text": it is <see cref="PrivacyPolicyHtml"/>, published by the app at /datenschutz.
    /// Both values are kept so switching the mode does not lose the other one.
    /// </summary>
    [RegularExpression("^(text|link)$")]
    public string PrivacyPolicyMode { get; set; } = "link";

    /// <summary>
    /// URL to the external privacy policy page (link mode).
    /// </summary>
    [MaxLength(2000)]
    public string PrivacyPolicyUrl { get; set; } = string.Empty;

    /// <summary>
    /// Own privacy policy as rich text (text mode). Sanitized on save.
    /// </summary>
    [MaxLength(100_000)]
    public string PrivacyPolicyHtml { get; set; } = string.Empty;

    /// <summary>
    /// Imprint of Vernetzt (optional): "link" to the external page <see cref="ImprintUrl"/> or "text"
    /// <see cref="ImprintHtml"/>, published by the app at /impressum. Both values are kept like for the privacy policy.
    /// </summary>
    [RegularExpression("^(text|link)$")]
    public string ImprintMode { get; set; } = "link";

    /// <summary>
    /// URL to the external imprint page (link mode).
    /// </summary>
    [MaxLength(2000)]
    public string ImprintUrl { get; set; } = string.Empty;

    /// <summary>
    /// Own imprint as rich text (text mode). Sanitized on save.
    /// </summary>
    [MaxLength(100_000)]
    public string ImprintHtml { get; set; } = string.Empty;
}
