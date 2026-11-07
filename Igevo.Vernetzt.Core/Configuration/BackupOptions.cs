namespace Igevo.Vernetzt.Core.Configuration;

/// <summary>
/// Settings for the automatic database backups (section "Backup").
/// Every value is optional; the defaults give a daily backup with 14 daily and 8 weekly copies.
/// </summary>
public class BackupOptions
{
    public const string SectionName = "Backup";

    /// <summary>
    /// Whether the scheduled daily backup runs. Manual backups from the admin area and
    /// backups before migrations are controlled separately.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Directory for the backup files. Relative paths are resolved against the content root.
    /// Empty: <c>App_Data/backups</c> below the content root.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Local time of day (HH:mm) at which the scheduled backup runs.
    /// </summary>
    public string Time { get; set; } = "03:00";

    /// <summary>
    /// Time zone for <see cref="Time"/> (IANA or Windows id).
    /// </summary>
    public string TimeZone { get; set; } = "Europe/Berlin";

    /// <summary>
    /// Number of days for which the newest scheduled backup of the day is kept.
    /// Manual and pre-migration backups are all kept for this many days.
    /// </summary>
    public int KeepDaily { get; set; } = 14;

    /// <summary>
    /// Number of calendar weeks for which the newest backup of the week is kept (in addition to the daily ones).
    /// </summary>
    public int KeepWeekly { get; set; } = 8;

    /// <summary>
    /// Whether a backup is created before pending database migrations are applied on startup.
    /// </summary>
    public bool BeforeMigration { get; set; } = true;
}
