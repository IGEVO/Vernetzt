using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Infrastructure.Data;
using Igevo.Vernetzt.Tests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Igevo.Vernetzt.Tests.Services;

/// <summary>
/// Data migration of the SeparateContextEmailVisibility migration: the role/office address keeps
/// exactly the visibility it had while one setting covered both.
/// </summary>
[TestClass]
public class ContextEmailVisibilityMigrationTests
{
    private const string PreviousMigration = "20261113231443_Locations";

    [TestMethod]
    public async Task Migration_CopiesTheAssignmentVisibilityToTheContextEmail()
    {
        await using var connection = SqliteTestDatabase.OpenConnection();
        var migrationOptions = SqliteTestDatabase.ConfigureSqlite(new DbContextOptionsBuilder<VernetztDbContext>(), connection)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        await using (var context = new VernetztDbContext(migrationOptions))
        {
            await context.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        }

        var club = Guid.NewGuid().ToString().ToUpperInvariant();
        var levels = new Dictionary<Guid, VisibilityLevel>
        {
            [Guid.NewGuid()] = VisibilityLevel.Internal,
            [Guid.NewGuid()] = VisibilityLevel.AllUsers,
            [Guid.NewGuid()] = VisibilityLevel.Public
        };

        Execute(connection, "PRAGMA foreign_keys = OFF;");
        Execute(connection, $"""
            INSERT INTO "Clubs" ("Id", "Name", "HasAvatar", "CreatedAt", "UpdatedAt")
            VALUES ('{club}', 'Club', 0, '2025-01-01', '2025-01-01');
            """);
        foreach (var (id, level) in levels)
        {
            Execute(connection, $"""
                INSERT INTO "ContactAssignments" ("Id", "UserId", "ClubId", "CanEdit", "ReceivesLocationInfo", "ContextEmail", "ContextEmailVerified", "Visibility", "ConfirmedAt", "AssignedAt")
                VALUES ('{id.ToString().ToUpperInvariant()}', '{Guid.NewGuid().ToString().ToUpperInvariant()}', '{club}', 0, 0, 'amt@verein.example', 1, {(int)level}, '2025-03-01 12:00:00', '2025-03-01 12:00:00');
                """);
        }

        await using (var context = new VernetztDbContext(migrationOptions))
        {
            await context.Database.MigrateAsync();
        }

        await using var verify = new VernetztDbContext(SqliteTestDatabase.CreateOptions(connection));
        var assignments = await verify.ContactAssignments.AsNoTracking().ToListAsync();

        Assert.HasCount(levels.Count, assignments);
        foreach (var assignment in assignments)
        {
            Assert.AreEqual(levels[assignment.Id], assignment.Visibility);
            Assert.AreEqual(levels[assignment.Id], assignment.ContextEmailVisibility);
        }
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
