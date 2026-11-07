using System.ComponentModel.DataAnnotations;

namespace Igevo.Vernetzt.Core.Configuration;

/// <summary>
/// Application-level configuration settings.
/// </summary>
public class AppSettings
{
    public const string SectionName = "App";

    /// <summary>
    /// Email address of the system administrator.
    /// This user will be automatically created on first startup if the database is empty.
    /// </summary>
    [Required]
    [EmailAddress]
    public string SystemAdminEmail { get; set; } = string.Empty;

    /// <summary>
    /// Public-facing base URL of the application.
    /// Used for email templates (logo, links) and file URL generation.
    /// </summary>
    [Required]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Optional base URL of the public portal (Igevo.Vernetzt.PublicWeb). If set, the admin UI
    /// links clubs, departments and groups to their public page.
    /// </summary>
    public string? PublicWebUrl { get; set; }

    /// <summary>
    /// Whether to seed development/sample data on first startup.
    /// Only set to true in development environments.
    /// </summary>
    public bool SeedDevelopmentData { get; set; } = false;

    /// <summary>
    /// Number of days until an invitation token expires.
    /// </summary>
    public int InvitationTokenExpirationDays { get; set; } = 7;

    /// <summary>
    /// Number of days to retain audit log entries before they are permanently deleted by the cleanup job.
    /// </summary>
    public int AuditLogRetentionDays { get; set; } = 90;

    /// <summary>
    /// Number of days to retain used or expired tokens (invitation, magic link, refresh, email relay)
    /// before they are permanently deleted by the cleanup job.
    /// </summary>
    public int ExpiredTokenRetentionDays { get; set; } = 30;
}
