namespace Igevo.Vernetzt.Core.DTOs.Settings;

/// <summary>
/// Contact person for questions about the application, as configured in the system settings.
/// </summary>
public class SupportContactDto
{
    /// <summary>
    /// Name of the contact person (empty if not configured).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Email address of the contact person (empty if not configured).
    /// </summary>
    public string Email { get; set; } = string.Empty;
}
