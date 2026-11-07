using Igevo.Vernetzt.Core.DTOs.DatabaseBackups;
using Igevo.Vernetzt.Core.Enums;

namespace Igevo.Vernetzt.Core.Interfaces.Services;

/// <summary>
/// Creates consistent copies of the SQLite database and removes old ones.
/// </summary>
public interface IDatabaseBackupService
{
    /// <summary>
    /// Creates a backup of the running database and applies the retention rules afterwards.
    /// Concurrent calls are serialized.
    /// </summary>
    Task<BackupFileDto> CreateBackupAsync(BackupTrigger trigger, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns configuration, the last failure and the existing backups.
    /// </summary>
    BackupStatusDto GetStatus();

    /// <summary>
    /// Next scheduled run after <paramref name="now"/> (UTC).
    /// </summary>
    DateTime GetNextScheduledRunUtc(DateTimeOffset now);
}
