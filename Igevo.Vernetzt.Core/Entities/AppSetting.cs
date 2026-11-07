using System.ComponentModel.DataAnnotations;

namespace Igevo.Vernetzt.Core.Entities;

/// <summary>
/// Key-value store for application settings manageable via the admin UI.
/// </summary>
public class AppSetting : BaseEntity
{
    /// <summary>
    /// Unique setting key (e.g., "ContactName", "ContactEmail", "PrivacyPolicyUrl").
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Setting value (unbounded: also holds rich text such as the portal imprint).
    /// </summary>
    public string Value { get; set; } = string.Empty;
}
