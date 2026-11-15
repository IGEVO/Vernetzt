using Igevo.Vernetzt.Core.Configuration;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Data;
using Igevo.Vernetzt.Infrastructure.Services;
using Igevo.Vernetzt.Tests.TestInfrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Igevo.Vernetzt.Tests.Services;

[TestClass]
public class DatabaseInitializationServiceTests
{
    [TestMethod]
    public async Task InitializeAsync_WithDevelopmentData_SeedsLocationsAndConsentStates()
    {
        using var connection = SqliteTestDatabase.OpenConnection();
        // Without the app's Identity options the model differs from the snapshot (Identity schema version),
        // which EF reports as "pending model changes"; see AssignmentVisibilityMigrationTests.
        var options = SqliteTestDatabase.ConfigureSqlite(new DbContextOptionsBuilder<VernetztDbContext>(), connection)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        await using var context = new VernetztDbContext(options);
        var service = new DatabaseInitializationService(
            context,
            Options.Create(new AppSettings { SystemAdminEmail = "admin@vernetzt.local", SeedDevelopmentData = true }),
            new Mock<IDatabaseBackupService>().Object,
            Options.Create(new BackupOptions()),
            NullLogger<DatabaseInitializationService>.Instance);

        await service.InitializeAsync();

        var locations = await context.Locations.Include(l => l.Clubs).Include(l => l.Departments).ToListAsync();
        Assert.HasCount(3, locations);
        Assert.IsTrue(locations.All(l => l.Clubs.Count + l.Departments.Count > 0), "Every demo location is in use");

        var assignments = await context.ContactAssignments.ToListAsync();
        Assert.IsTrue(assignments.Any(a => a.IsConfirmed && a.Visibility == VisibilityLevel.Public));
        Assert.IsTrue(assignments.Any(a => !a.IsConfirmed), "Demo data contains a pending assignment");

        // A second start must not seed again.
        await service.InitializeAsync();
        Assert.AreEqual(3, await context.Locations.CountAsync());
    }
}
