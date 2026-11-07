using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Igevo.Vernetzt.Tests.TestInfrastructure;

/// <summary>
/// In-memory SQLite databases for tests: the same engine as production (constraints, unique and foreign
/// keys are enforced), isolated per test. The database lives as long as its connection is open.
/// </summary>
public static class SqliteTestDatabase
{
    /// <summary>
    /// Opens a connection to a new, empty in-memory database.
    /// </summary>
    public static SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        return connection;
    }

    /// <summary>
    /// Context options for an open connection.
    /// </summary>
    public static DbContextOptions<VernetztDbContext> CreateOptions(SqliteConnection connection) =>
        ConfigureSqlite(new DbContextOptionsBuilder<VernetztDbContext>(), connection).Options;

    /// <summary>
    /// Configures an options builder (e.g. inside <c>AddDbContext</c>) for an open connection.
    /// </summary>
    public static TBuilder ConfigureSqlite<TBuilder>(TBuilder builder, SqliteConnection connection)
        where TBuilder : DbContextOptionsBuilder
    {
        builder.UseSqlite(connection).EnableDetailedErrors().EnableSensitiveDataLogging();
        return builder;
    }

    /// <summary>
    /// Creates the schema in the database behind <paramref name="connection"/>.
    /// </summary>
    public static void CreateSchema(SqliteConnection connection)
    {
        using var context = new VernetztDbContext(CreateOptions(connection));
        context.Database.EnsureCreated();
    }

    /// <summary>
    /// A context on its own new database with the schema created. Disposing the context closes the database.
    /// </summary>
    public static VernetztDbContext CreateContext()
    {
        var connection = OpenConnection();
        CreateSchema(connection);
        return new OwningVernetztDbContext(CreateOptions(connection), connection);
    }

    private sealed class OwningVernetztDbContext(DbContextOptions<VernetztDbContext> options, SqliteConnection connection)
        : VernetztDbContext(options)
    {
        public override void Dispose()
        {
            base.Dispose();
            connection.Dispose();
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
