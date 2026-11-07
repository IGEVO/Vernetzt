using Igevo.Vernetzt.Core.Configuration;
using Igevo.Vernetzt.Core.DTOs.DatabaseBackups;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Infrastructure.Data;
using Igevo.Vernetzt.Infrastructure.Observability;
using Igevo.Vernetzt.Infrastructure.Services;
using Igevo.Vernetzt.Tests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Igevo.Vernetzt.Tests.Services;

[TestClass]
public class DatabaseBackupServiceTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private string _workDirectory = null!;
    private string _databasePath = null!;
    private string _backupDirectory = null!;
    private ManualTimeProvider _time = null!;

    [TestInitialize]
    public void Setup()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), $"vernetzt-backup-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDirectory);
        _databasePath = Path.Combine(_workDirectory, "vernetzt.db");
        _backupDirectory = Path.Combine(_workDirectory, "backups");
        _time = new ManualTimeProvider(new DateTimeOffset(2026, 11, 5, 2, 0, 0, TimeSpan.Zero));

        using var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE Items (Name TEXT); INSERT INTO Items VALUES ('Vereinsheim');";
        command.ExecuteNonQuery();
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_workDirectory, recursive: true);
    }

    [TestMethod]
    public async Task CreateBackup_WritesConsistentCopyWithData()
    {
        var service = CreateService();

        var backup = await service.CreateBackupAsync(BackupTrigger.Manual);

        Assert.AreEqual("vernetzt-20261105-020000-manual.db", backup.FileName);
        Assert.AreEqual(BackupTrigger.Manual, backup.Trigger);
        Assert.IsTrue(backup.SizeBytes > 0);
        Assert.IsFalse(Directory.EnumerateFiles(_backupDirectory, "*.tmp").Any(), "no temporary file is left behind");

        using var connection = new SqliteConnection($"Data Source={Path.Combine(_backupDirectory, backup.FileName)};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Name FROM Items";
        Assert.AreEqual("Vereinsheim", command.ExecuteScalar());
    }

    [TestMethod]
    public async Task CreateBackup_RecordsMetricsAndLastSuccessGauge()
    {
        using var recorder = new MetricsRecorder();
        var service = CreateService(metrics: recorder.Metrics);

        Assert.IsNull(recorder.ObserveGauge("vernetzt.backup.last_success_timestamp"), "no backup yet");

        await service.CreateBackupAsync(BackupTrigger.Manual);

        Assert.AreEqual(1, recorder.Sum("vernetzt.backup.runs", ("trigger", "manual"), ("outcome", "success")));
        Assert.AreEqual(_time.GetUtcNow().ToUnixTimeSeconds(), recorder.ObserveGauge("vernetzt.backup.last_success_timestamp"));
        Assert.IsNull(recorder.ObserveGauge("vernetzt.backup.last_failure_timestamp"));
    }

    [TestMethod]
    public async Task CreateBackup_Failure_RecordsFailureMetric()
    {
        using var recorder = new MetricsRecorder();
        _databasePath = Path.Combine(_workDirectory, "missing", "vernetzt.db");
        var service = CreateService(metrics: recorder.Metrics);

        await Assert.ThrowsAsync<SqliteException>(() => service.CreateBackupAsync(BackupTrigger.Scheduled));

        Assert.AreEqual(1, recorder.Sum("vernetzt.backup.runs", ("trigger", "scheduled"), ("outcome", "failure")));
        Assert.AreEqual(_time.GetUtcNow().ToUnixTimeSeconds(), recorder.ObserveGauge("vernetzt.backup.last_failure_timestamp"));
    }

    [TestMethod]
    public async Task CreateBackup_TwiceInSameSecond_UsesDistinctFileNames()
    {
        var service = CreateService();

        var first = await service.CreateBackupAsync(BackupTrigger.Manual);
        var second = await service.CreateBackupAsync(BackupTrigger.Manual);

        Assert.AreNotEqual(first.FileName, second.FileName);
        Assert.AreEqual(2, service.GetStatus().Backups.Count);
    }

    [TestMethod]
    public async Task CreateBackup_AppliesRetention()
    {
        var service = CreateService(keepDaily: 2, keepWeekly: 0);

        for (var day = 0; day < 4; day++)
        {
            await service.CreateBackupAsync(BackupTrigger.Scheduled);
            _time.Advance(TimeSpan.FromDays(1));
        }

        var remaining = service.GetStatus().Backups.Select(b => b.FileName).ToList();
        CollectionAssert.AreEqual(
            new[] { "vernetzt-20261108-020000-scheduled.db", "vernetzt-20261107-020000-scheduled.db" },
            remaining);
    }

    [TestMethod]
    public async Task CreateBackup_WhenTargetIsNotWritable_RecordsFailure()
    {
        // A file where the backup directory should be makes the backup fail.
        File.WriteAllText(_backupDirectory, "not a directory");
        var service = CreateService();

        await Assert.ThrowsExactlyAsync<IOException>(() => service.CreateBackupAsync(BackupTrigger.Manual));

        var status = service.GetStatus();
        Assert.AreEqual(_time.GetUtcNow().UtcDateTime, status.LastFailureUtc);
        Assert.IsFalse(string.IsNullOrEmpty(status.LastError));
    }

    [TestMethod]
    public void GetStatus_IgnoresForeignFiles_AndListsNewestFirst()
    {
        Directory.CreateDirectory(_backupDirectory);
        File.WriteAllText(Path.Combine(_backupDirectory, "vernetzt-20261101-030000-scheduled.db"), "x");
        File.WriteAllText(Path.Combine(_backupDirectory, "vernetzt-20261102-030000-pre-migration.db"), "x");
        File.WriteAllText(Path.Combine(_backupDirectory, "notes.txt"), "x");
        File.WriteAllText(Path.Combine(_backupDirectory, "vernetzt-copy.db"), "x");

        var status = CreateService().GetStatus();

        Assert.AreEqual(2, status.Backups.Count);
        Assert.AreEqual(BackupTrigger.PreMigration, status.Backups[0].Trigger);
        Assert.AreEqual(new DateTime(2026, 11, 2, 3, 0, 0, DateTimeKind.Utc), status.Backups[0].CreatedUtc);
        Assert.AreEqual(BackupTrigger.Scheduled, status.Backups[1].Trigger);
    }

    [TestMethod]
    public void SelectBackupsToDelete_KeepsNewestPerDayAndPerWeek()
    {
        // Two scheduled backups on Nov 5, one on each of Nov 4, Oct 28 and Oct 20.
        var backups = new[]
        {
            Backup(2026, 11, 5, 14), Backup(2026, 11, 5, 2), Backup(2026, 11, 4, 2),
            Backup(2026, 10, 28, 2), Backup(2026, 10, 20, 2)
        };

        var deleted = DatabaseBackupService.SelectBackupsToDelete(backups, keepDaily: 2, keepWeekly: 2, Berlin, Now);

        // Daily: Nov 5 14:00, Nov 4. Weekly (ISO weeks 45 and 44): Nov 5 14:00, Oct 28.
        CollectionAssert.AreEquivalent(new[] { backups[1], backups[4] }, deleted.ToList());
    }

    [TestMethod]
    public void SelectBackupsToDelete_AlwaysKeepsNewest_EvenWithZeroRetention()
    {
        var backups = new[] { Backup(2026, 11, 5, 2), Backup(2026, 11, 4, 2) };

        var deleted = DatabaseBackupService.SelectBackupsToDelete(backups, keepDaily: 0, keepWeekly: 0, Berlin, Now);

        CollectionAssert.AreEqual(new[] { backups[1] }, deleted.ToList());
    }

    [TestMethod]
    public void SelectBackupsToDelete_KeepsAllManualAndPreMigrationBackups_ForKeepDailyDays()
    {
        var backups = new[]
        {
            Backup(2026, 11, 5, 12, BackupTrigger.Manual),
            Backup(2026, 11, 5, 10, BackupTrigger.PreMigration),
            Backup(2026, 11, 5, 2),
            Backup(2026, 10, 1, 10, BackupTrigger.PreMigration)
        };

        var deleted = DatabaseBackupService.SelectBackupsToDelete(backups, keepDaily: 14, keepWeekly: 0, Berlin, Now);

        // The later manual backup does not replace the pre-migration copy of the same day;
        // the old pre-migration backup is outside the 14 days.
        CollectionAssert.AreEqual(new[] { backups[3] }, deleted.ToList());
    }

    [TestMethod]
    public void GetNextScheduledRun_UsesConfiguredLocalTime()
    {
        var service = CreateService(time: "03:00");

        // 02:00 UTC = 03:00 Berlin (CET) on Nov 5: exactly the scheduled time, so the next run is tomorrow.
        Assert.AreEqual(new DateTime(2026, 11, 6, 2, 0, 0, DateTimeKind.Utc),
            service.GetNextScheduledRunUtc(new DateTimeOffset(2026, 11, 5, 2, 0, 0, TimeSpan.Zero)));

        // 01:00 UTC = 02:00 Berlin: the run is still due today.
        Assert.AreEqual(new DateTime(2026, 11, 5, 2, 0, 0, DateTimeKind.Utc),
            service.GetNextScheduledRunUtc(new DateTimeOffset(2026, 11, 5, 1, 0, 0, TimeSpan.Zero)));

        // Summer time (CEST, UTC+2).
        Assert.AreEqual(new DateTime(2026, 7, 1, 1, 0, 0, DateTimeKind.Utc),
            service.GetNextScheduledRunUtc(new DateTimeOffset(2026, 6, 30, 12, 0, 0, TimeSpan.Zero)));
    }

    [TestMethod]
    public void GetNextScheduledRun_OnSpringForward_RunsAnHourLater()
    {
        // 2026-03-29: clocks jump from 02:00 to 03:00 in Berlin, 02:30 does not exist.
        var service = CreateService(time: "02:30");

        var next = service.GetNextScheduledRunUtc(new DateTimeOffset(2026, 3, 28, 12, 0, 0, TimeSpan.Zero));

        Assert.AreEqual(new DateTime(2026, 3, 29, 1, 30, 0, DateTimeKind.Utc), next);
    }

    [TestMethod]
    public void Constructor_RejectsInvalidTime()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => CreateService(time: "3 Uhr"));
    }

    [TestMethod]
    public void PrepareConnectionString_MakesRelativePathAbsolute_AndCreatesDirectory()
    {
        var connectionString = SqliteDatabase.PrepareConnectionString("Data Source=data/sub/vernetzt.db", _workDirectory);

        var expected = Path.Combine(_workDirectory, "data", "sub", "vernetzt.db");
        Assert.AreEqual(expected, SqliteDatabase.GetDatabaseFilePath(connectionString));
        Assert.IsTrue(Directory.Exists(Path.GetDirectoryName(expected)));
    }

    [TestMethod]
    public void DeleteStaleTemporaryFiles_RemovesOnlyOldEntriesWithPrefix()
    {
        var old = DateTime.UtcNow.AddHours(-2);
        var staleFile = Path.Combine(_workDirectory, "vernetzt-test-old.db");
        var staleDirectory = Path.Combine(_workDirectory, "vernetzt-test-old.db-storage");
        var freshFile = Path.Combine(_workDirectory, "vernetzt-test-new.db");
        var otherFile = Path.Combine(_workDirectory, "other.db");
        File.WriteAllText(staleFile, "x");
        Directory.CreateDirectory(Path.Combine(staleDirectory, "avatars"));
        File.WriteAllText(freshFile, "x");
        File.WriteAllText(otherFile, "x");
        File.SetLastWriteTimeUtc(staleFile, old);
        File.SetLastWriteTimeUtc(otherFile, old);
        Directory.SetLastWriteTimeUtc(staleDirectory, old);

        SqliteDatabase.DeleteStaleTemporaryFiles(_workDirectory, "vernetzt-test-", DateTime.UtcNow.AddHours(-1));

        Assert.IsFalse(File.Exists(staleFile));
        Assert.IsFalse(Directory.Exists(staleDirectory));
        Assert.IsTrue(File.Exists(freshFile));
        Assert.IsTrue(File.Exists(otherFile));
    }

    private DatabaseBackupService CreateService(int keepDaily = 14, int keepWeekly = 8, string time = "03:00", VernetztMetrics? metrics = null) =>
        new(
            new DatabaseBackupSettings(
                $"Data Source={_databasePath}",
                _backupDirectory,
                new BackupOptions { KeepDaily = keepDaily, KeepWeekly = keepWeekly, Time = time, TimeZone = "Europe/Berlin" }),
            NullLogger<DatabaseBackupService>.Instance,
            _time,
            metrics);

    private static readonly DateTime Now = new(2026, 11, 5, 15, 0, 0, DateTimeKind.Utc);

    private static BackupFileDto Backup(int year, int month, int day, int hourUtc, BackupTrigger trigger = BackupTrigger.Scheduled) => new()
    {
        FileName = $"vernetzt-{year:0000}{month:00}{day:00}-{hourUtc:00}0000-{trigger}.db",
        CreatedUtc = new DateTime(year, month, day, hourUtc, 0, 0, DateTimeKind.Utc),
        Trigger = trigger
    };

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
