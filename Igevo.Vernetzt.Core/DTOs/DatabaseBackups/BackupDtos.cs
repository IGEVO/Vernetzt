using Igevo.Vernetzt.Core.Enums;

namespace Igevo.Vernetzt.Core.DTOs.DatabaseBackups;

/// <summary>
/// A database backup file.
/// </summary>
public class BackupFileDto
{
    /// <summary>File name inside the backup directory.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>When the backup was created (UTC).</summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>File size in bytes.</summary>
    public long SizeBytes { get; set; }

    /// <summary>What caused the backup.</summary>
    public BackupTrigger Trigger { get; set; }
}

/// <summary>
/// Backup configuration, last results and existing backup files.
/// </summary>
public class BackupStatusDto
{
    /// <summary>Whether the scheduled daily backup is enabled.</summary>
    public bool ScheduleEnabled { get; set; }

    /// <summary>Local time of day of the scheduled backup (HH:mm).</summary>
    public string ScheduleTime { get; set; } = string.Empty;

    /// <summary>Time zone of <see cref="ScheduleTime"/>.</summary>
    public string TimeZone { get; set; } = string.Empty;

    /// <summary>Next scheduled run (UTC), null when the schedule is disabled.</summary>
    public DateTime? NextRunUtc { get; set; }

    /// <summary>Number of days with a kept daily backup.</summary>
    public int KeepDaily { get; set; }

    /// <summary>Number of weeks with a kept weekly backup.</summary>
    public int KeepWeekly { get; set; }

    /// <summary>Whether a backup is created before migrations.</summary>
    public bool BeforeMigration { get; set; }

    /// <summary>Backup directory inside the container/host.</summary>
    public string Directory { get; set; } = string.Empty;

    /// <summary>Last failed backup attempt since the application started (UTC).</summary>
    public DateTime? LastFailureUtc { get; set; }

    /// <summary>Error message of the last failed attempt.</summary>
    public string? LastError { get; set; }

    /// <summary>Existing backups, newest first.</summary>
    public List<BackupFileDto> Backups { get; set; } = [];
}
