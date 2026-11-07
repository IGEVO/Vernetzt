using System.Globalization;
using System.Text.RegularExpressions;
using Igevo.Vernetzt.Core.Configuration;
using Igevo.Vernetzt.Core.DTOs.DatabaseBackups;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Observability;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Igevo.Vernetzt.Infrastructure.Services;

/// <summary>
/// Resolved settings for <see cref="DatabaseBackupService"/>.
/// </summary>
/// <param name="ConnectionString">Connection string of the database to back up.</param>
/// <param name="Directory">Absolute backup directory.</param>
/// <param name="Options">Backup options from the configuration.</param>
public sealed record DatabaseBackupSettings(string ConnectionString, string Directory, BackupOptions Options);

/// <summary>
/// Backs up the SQLite database with <c>VACUUM INTO</c>, which writes a consistent copy while the
/// application keeps running. Files are written under a temporary name and renamed when complete.
/// </summary>
public partial class DatabaseBackupService : IDatabaseBackupService
{
    private const string FilePrefix = "vernetzt-";
    private const string TimestampFormat = "yyyyMMdd-HHmmss";

    private readonly DatabaseBackupSettings _settings;
    private readonly ILogger<DatabaseBackupService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TimeOnly _scheduleTime;
    private readonly TimeZoneInfo _timeZone;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly VernetztMetrics? _metrics;

    private DateTime? _lastFailureUtc;
    private string? _lastError;

    public DatabaseBackupService(
        DatabaseBackupSettings settings,
        ILogger<DatabaseBackupService> logger,
        TimeProvider timeProvider,
        VernetztMetrics? metrics = null)
    {
        _settings = settings;
        _logger = logger;
        _timeProvider = timeProvider;
        _metrics = metrics;

        if (!TimeOnly.TryParseExact(settings.Options.Time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _scheduleTime))
        {
            throw new InvalidOperationException($"Backup:Time must have the format HH:mm (got '{settings.Options.Time}').");
        }

        try
        {
            _timeZone = TimeZoneInfo.FindSystemTimeZoneById(settings.Options.TimeZone);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidOperationException($"Backup:TimeZone '{settings.Options.TimeZone}' is not a known time zone.", ex);
        }

        // Only while scheduled backups are on: alerting on backup age makes no sense otherwise.
        if (settings.Options.Enabled)
        {
            _metrics?.ObserveBackupState(() => (
                ListBackups().Select(b => (DateTime?)b.CreatedUtc).Max(),
                _lastFailureUtc));
        }
    }

    /// <inheritdoc />
    public async Task<BackupFileDto> CreateBackupAsync(BackupTrigger trigger, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        string? tempPath = null;
        try
        {
            Directory.CreateDirectory(_settings.Directory);

            var createdUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var finalPath = GetFreeFilePath(createdUtc, trigger);
            tempPath = finalPath + ".tmp";
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            await using (var connection = new SqliteConnection(_settings.ConnectionString))
            {
                await connection.OpenAsync(cancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText = "VACUUM INTO $path";
                command.Parameters.AddWithValue("$path", tempPath);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            File.Move(tempPath, finalPath);
            tempPath = null;

            var backup = new BackupFileDto
            {
                FileName = Path.GetFileName(finalPath),
                CreatedUtc = createdUtc,
                SizeBytes = new FileInfo(finalPath).Length,
                Trigger = trigger
            };

            _logger.LogInformation("Database backup created: {FileName} ({SizeBytes} bytes, {Trigger})",
                backup.FileName, backup.SizeBytes, trigger);
            _metrics?.BackupCompleted(trigger.ToString(), success: true);

            ApplyRetention();
            return backup;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _lastFailureUtc = _timeProvider.GetUtcNow().UtcDateTime;
            _lastError = ex.Message;
            _logger.LogError(ex, "Database backup failed ({Trigger})", trigger);
            _metrics?.BackupCompleted(trigger.ToString(), success: false);
            throw;
        }
        finally
        {
            if (tempPath != null)
            {
                TryDelete(tempPath);
            }

            _lock.Release();
        }
    }

    /// <inheritdoc />
    public BackupStatusDto GetStatus()
    {
        var options = _settings.Options;
        return new BackupStatusDto
        {
            ScheduleEnabled = options.Enabled,
            ScheduleTime = _scheduleTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            TimeZone = options.TimeZone,
            NextRunUtc = options.Enabled ? GetNextScheduledRunUtc(_timeProvider.GetUtcNow()) : null,
            KeepDaily = EffectiveKeepDaily,
            KeepWeekly = Math.Max(0, options.KeepWeekly),
            BeforeMigration = options.BeforeMigration,
            Directory = _settings.Directory,
            LastFailureUtc = _lastFailureUtc,
            LastError = _lastError,
            Backups = ListBackups()
        };
    }

    /// <inheritdoc />
    public DateTime GetNextScheduledRunUtc(DateTimeOffset now)
    {
        var localNow = TimeZoneInfo.ConvertTime(now, _timeZone).DateTime;
        var candidate = localNow.Date + _scheduleTime.ToTimeSpan();
        if (candidate <= localNow)
        {
            candidate = candidate.AddDays(1);
        }

        // The configured time does not exist on the day the clocks go forward: run an hour later.
        if (_timeZone.IsInvalidTime(candidate))
        {
            candidate = candidate.AddHours(1);
        }

        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(candidate, DateTimeKind.Unspecified), _timeZone);
    }

    /// <summary>
    /// Backups that the retention rules remove. Scheduled backups: of each local day only the newest is kept,
    /// for the <paramref name="keepDaily"/> most recent days, plus the newest of each of the
    /// <paramref name="keepWeekly"/> most recent ISO weeks. Manual and pre-migration backups are never thinned
    /// out (a later backup must not replace the copy taken before an update) and are kept for
    /// <paramref name="keepDaily"/> days.
    /// </summary>
    internal static IReadOnlyList<BackupFileDto> SelectBackupsToDelete(
        IEnumerable<BackupFileDto> backups, int keepDaily, int keepWeekly, TimeZoneInfo timeZone, DateTime nowUtc)
    {
        keepDaily = Math.Max(1, keepDaily);
        var all = backups.ToList();
        var scheduled = all.Where(b => b.Trigger == BackupTrigger.Scheduled).OrderByDescending(b => b.CreatedUtc).ToList();
        DateTime LocalDate(BackupFileDto b) =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(b.CreatedUtc, DateTimeKind.Utc), timeZone).Date;

        var keep = new HashSet<BackupFileDto>();

        foreach (var day in scheduled.GroupBy(LocalDate).Take(keepDaily))
        {
            keep.Add(day.First());
        }

        foreach (var week in scheduled
                     .GroupBy(b => (ISOWeek.GetYear(LocalDate(b)), ISOWeek.GetWeekOfYear(LocalDate(b))))
                     .Take(Math.Max(0, keepWeekly)))
        {
            keep.Add(week.First());
        }

        var keepOnDemandSince = nowUtc.AddDays(-keepDaily);
        foreach (var backup in all.Where(b => b.Trigger != BackupTrigger.Scheduled && b.CreatedUtc >= keepOnDemandSince))
        {
            keep.Add(backup);
        }

        // Never delete the newest backup, whatever the settings.
        var newest = all.MaxBy(b => b.CreatedUtc);
        if (newest != null)
        {
            keep.Add(newest);
        }

        return all.Where(b => !keep.Contains(b)).OrderByDescending(b => b.CreatedUtc).ToList();
    }

    /// <summary>
    /// Parses a backup file name created by this service; null for any other file.
    /// </summary>
    internal static (DateTime CreatedUtc, BackupTrigger Trigger)? ParseFileName(string fileName)
    {
        var match = BackupFileNameRegex().Match(fileName);
        if (!match.Success ||
            !DateTime.TryParseExact(match.Groups["timestamp"].Value, TimestampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var createdUtc))
        {
            return null;
        }

        var trigger = match.Groups["trigger"].Value switch
        {
            "manual" => BackupTrigger.Manual,
            "pre-migration" => BackupTrigger.PreMigration,
            _ => BackupTrigger.Scheduled
        };
        return (createdUtc, trigger);
    }

    private int EffectiveKeepDaily => Math.Max(1, _settings.Options.KeepDaily);

    private List<BackupFileDto> ListBackups()
    {
        if (!Directory.Exists(_settings.Directory))
        {
            return [];
        }

        var backups = new List<BackupFileDto>();
        foreach (var file in new DirectoryInfo(_settings.Directory).EnumerateFiles(FilePrefix + "*.db"))
        {
            var parsed = ParseFileName(file.Name);
            if (parsed is { } info)
            {
                backups.Add(new BackupFileDto
                {
                    FileName = file.Name,
                    CreatedUtc = info.CreatedUtc,
                    SizeBytes = file.Length,
                    Trigger = info.Trigger
                });
            }
        }

        return backups.OrderByDescending(b => b.CreatedUtc).ThenByDescending(b => b.FileName, StringComparer.Ordinal).ToList();
    }

    private void ApplyRetention()
    {
        var toDelete = SelectBackupsToDelete(
            ListBackups(), EffectiveKeepDaily, _settings.Options.KeepWeekly, _timeZone, _timeProvider.GetUtcNow().UtcDateTime);
        foreach (var backup in toDelete)
        {
            if (TryDelete(Path.Combine(_settings.Directory, backup.FileName)))
            {
                _logger.LogInformation("Old database backup deleted: {FileName}", backup.FileName);
            }
        }

        // Leftovers of interrupted backups (e.g. container stopped while writing).
        var staleBefore = _timeProvider.GetUtcNow().UtcDateTime.AddDays(-1);
        foreach (var file in new DirectoryInfo(_settings.Directory).EnumerateFiles(FilePrefix + "*.db.tmp"))
        {
            if (file.LastWriteTimeUtc < staleBefore)
            {
                TryDelete(file.FullName);
            }
        }
    }

    private string GetFreeFilePath(DateTime createdUtc, BackupTrigger trigger)
    {
        var timestamp = createdUtc.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        var suffix = trigger switch
        {
            BackupTrigger.Manual => "manual",
            BackupTrigger.PreMigration => "pre-migration",
            _ => "scheduled"
        };

        var path = Path.Combine(_settings.Directory, $"{FilePrefix}{timestamp}-{suffix}.db");
        for (var counter = 2; File.Exists(path); counter++)
        {
            path = Path.Combine(_settings.Directory, $"{FilePrefix}{timestamp}-{counter}-{suffix}.db");
        }

        return path;
    }

    private bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not delete backup file {Path}", path);
            return false;
        }
    }

    [GeneratedRegex(@"^vernetzt-(?<timestamp>\d{8}-\d{6})(?:-\d+)?-(?<trigger>scheduled|manual|pre-migration)\.db$")]
    private static partial Regex BackupFileNameRegex();
}
