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
    /// URL to the privacy policy page.
    /// </summary>
    [Required]
    [Url]
    [MaxLength(2000)]
    public string PrivacyPolicyUrl { get; set; } = string.Empty;
}
