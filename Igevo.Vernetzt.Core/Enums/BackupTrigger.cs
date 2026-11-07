namespace Igevo.Vernetzt.Core.Enums;

/// <summary>
/// What caused a database backup.
/// </summary>
public enum BackupTrigger
{
    /// <summary>The daily scheduled backup.</summary>
    Scheduled = 0,

    /// <summary>Started by a system administrator.</summary>
    Manual = 1,

    /// <summary>Created on startup before pending migrations were applied.</summary>
    PreMigration = 2
}
