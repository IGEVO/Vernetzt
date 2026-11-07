using Microsoft.Data.Sqlite;

namespace Igevo.Vernetzt.Infrastructure.Data;

/// <summary>
/// Helpers for the SQLite database file.
/// </summary>
public static class SqliteDatabase
{
    /// <summary>
    /// Makes a relative <c>Data Source</c> absolute (relative to <paramref name="contentRootPath"/>)
    /// and creates the directory of the database file.
    /// </summary>
    public static string PrepareConnectionString(string connectionString, string contentRootPath)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (IsInMemory(builder))
        {
            return builder.ToString();
        }

        if (string.IsNullOrWhiteSpace(builder.DataSource))
        {
            throw new InvalidOperationException("The connection string 'DefaultConnection' has no 'Data Source'.");
        }

        if (!Path.IsPathRooted(builder.DataSource))
        {
            builder.DataSource = Path.GetFullPath(Path.Combine(contentRootPath, builder.DataSource));
        }

        var directory = Path.GetDirectoryName(builder.DataSource);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Path of the database file, or null for an in-memory database.
    /// </summary>
    public static string? GetDatabaseFilePath(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        return IsInMemory(builder) ? null : builder.DataSource;
    }

    /// <summary>
    /// Deletes the database file together with its WAL and shared-memory files.
    /// </summary>
    public static void DeleteDatabaseFiles(string databaseFilePath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { databaseFilePath, databaseFilePath + "-wal", databaseFilePath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>
    /// Deletes files and directories in <paramref name="directory"/> starting with <paramref name="prefix"/> that were
    /// last written before <paramref name="olderThanUtc"/>: leftovers of temporary test databases whose process was
    /// killed before it could clean up. Entries still in use are skipped.
    /// </summary>
    public static void DeleteStaleTemporaryFiles(string directory, string prefix, DateTime olderThanUtc)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos(prefix + "*"))
        {
            try
            {
                if (entry.LastWriteTimeUtc >= olderThanUtc)
                {
                    continue;
                }

                if (entry is DirectoryInfo dir)
                {
                    dir.Delete(recursive: true);
                }
                else
                {
                    entry.Delete();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // In use or not ours: leave it.
            }
        }
    }

    private static bool IsInMemory(SqliteConnectionStringBuilder builder) =>
        builder.Mode == SqliteOpenMode.Memory ||
        string.Equals(builder.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase);
}
