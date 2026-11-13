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
/// Data migration of the AssignmentVisibilityConsent migration: existing assignments become confirmed and take
/// over the visibility of their club/department; a stricter role/office address visibility is kept.
/// </summary>
[TestClass]
public class AssignmentVisibilityMigrationTests
{
    private const string PreviousMigration = "20261107132234_OrganizationShortName";

    [TestMethod]
    public async Task Migration_TakesOverContainerVisibilityAndConfirmsExistingAssignments()
    {
        await using var connection = SqliteTestDatabase.OpenConnection();
        // Without the app's Identity options the model differs from the snapshot (Identity schema version),
        // which EF reports as "pending model changes"; the migrations themselves are what is tested here.
        var migrationOptions = SqliteTestDatabase.ConfigureSqlite(new DbContextOptionsBuilder<VernetztDbContext>(), connection)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        await using (var context = new VernetztDbContext(migrationOptions))
        {
            await context.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        }

        var publicClub = Guid.NewGuid();
        var legacyInternalClub = Guid.NewGuid();
        var allUsersDepartment = Guid.NewGuid();
        var assignedAt = new DateTime(2025, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var plain = Guid.NewGuid();
        var stricterRoleAddress = Guid.NewGuid();
        var legacyInternal = Guid.NewGuid();
        var departmentAssignment = Guid.NewGuid();

        Execute(connection, "PRAGMA foreign_keys = OFF;");
        Execute(connection, $"""
            INSERT INTO "Clubs" ("Id", "Name", "ContactPersonsVisibility", "HasAvatar", "CreatedAt", "UpdatedAt")
            VALUES ('{publicClub.ToString().ToUpperInvariant()}', 'Public', 2, 0, '2025-01-01', '2025-01-01'),
                   ('{legacyInternalClub.ToString().ToUpperInvariant()}', 'Legacy', 0, 0, '2025-01-01', '2025-01-01');
            INSERT INTO "Departments" ("Id", "Name", "ClubId", "ContactPersonsVisibility", "HasAvatar", "CreatedAt", "UpdatedAt")
            VALUES ('{allUsersDepartment.ToString().ToUpperInvariant()}', 'Jugend', '{publicClub.ToString().ToUpperInvariant()}', 1, 0, '2025-01-01', '2025-01-01');
            INSERT INTO "ContactAssignments" ("Id", "UserId", "ClubId", "DepartmentId", "CanEdit", "ContextEmail", "ContextEmailVerified", "ContextEmailVisibility", "AssignedAt")
            VALUES ('{plain.ToString().ToUpperInvariant()}', '{Guid.NewGuid().ToString().ToUpperInvariant()}', '{publicClub.ToString().ToUpperInvariant()}', NULL, 1, NULL, 0, 1, '{assignedAt:yyyy-MM-dd HH:mm:ss}'),
                   ('{stricterRoleAddress.ToString().ToUpperInvariant()}', '{Guid.NewGuid().ToString().ToUpperInvariant()}', '{publicClub.ToString().ToUpperInvariant()}', NULL, 0, 'vorstand@verein.example', 1, 0, '{assignedAt:yyyy-MM-dd HH:mm:ss}'),
                   ('{legacyInternal.ToString().ToUpperInvariant()}', '{Guid.NewGuid().ToString().ToUpperInvariant()}', '{legacyInternalClub.ToString().ToUpperInvariant()}', NULL, 0, NULL, 0, 2, '{assignedAt:yyyy-MM-dd HH:mm:ss}'),
                   ('{departmentAssignment.ToString().ToUpperInvariant()}', '{Guid.NewGuid().ToString().ToUpperInvariant()}', NULL, '{allUsersDepartment.ToString().ToUpperInvariant()}', 0, 'jugend@verein.example', 1, 2, '{assignedAt:yyyy-MM-dd HH:mm:ss}');
            """);

        await using (var context = new VernetztDbContext(migrationOptions))
        {
            await context.Database.MigrateAsync();
        }

        await using var verify = new VernetztDbContext(SqliteTestDatabase.CreateOptions(connection));
        var assignments = await verify.ContactAssignments.AsNoTracking().ToDictionaryAsync(a => a.Id);

        Assert.AreEqual(VisibilityLevel.Public, assignments[plain].Visibility, "Public club stays public.");
        Assert.AreEqual(VisibilityLevel.Internal, assignments[stricterRoleAddress].Visibility,
            "A stricter role address visibility must not become more visible.");
        Assert.AreEqual(VisibilityLevel.AllUsers, assignments[legacyInternal].Visibility, "Legacy Internal meant AllUsers.");
        Assert.AreEqual(VisibilityLevel.AllUsers, assignments[departmentAssignment].Visibility,
            "The stricter department visibility wins over a public role address.");
        Assert.IsTrue(assignments.Values.All(a => a.ConfirmedAt == a.AssignedAt), "Existing assignments count as confirmed.");
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
