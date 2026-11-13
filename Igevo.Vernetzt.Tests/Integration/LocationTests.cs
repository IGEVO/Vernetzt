using Igevo.Vernetzt.Core.DTOs.Locations;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace Igevo.Vernetzt.Tests.Integration;

/// <summary>
/// Locations ("Standorte"): central list, links to clubs/departments/groups and recipient candidates.
/// </summary>
[TestClass]
public class LocationTests : IntegrationTestBase
{
    private LocationService _locationService = null!;
    private ClubService _clubService = null!;
    private DepartmentService _departmentService = null!;

    [TestInitialize]
    public void Setup()
    {
        _locationService = new LocationService(DbContext, AuditLogRepository);
        _clubService = new ClubService(
            ClubRepository,
            UserRepository,
            AuthorizationServiceMock.Object,
            AuditLogRepository,
            EmailServiceMock.Object,
            new Mock<IAvatarService>().Object,
            DbContext,
            new Mock<ILogger<ClubService>>().Object);
        _departmentService = new DepartmentService(
            DepartmentRepository,
            ClubRepository,
            UserRepository,
            AuthorizationServiceMock.Object,
            AuditLogRepository,
            EmailServiceMock.Object,
            new Mock<IAvatarService>().Object,
            DbContext,
            new Mock<ILogger<DepartmentService>>().Object);

        AuthorizationServiceMock.Setup(a => a.CanAccessClubAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        AuthorizationServiceMock.Setup(a => a.CanAccessDepartmentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private Task<LocationDto> CreateLocationAsync(string name) =>
        _locationService.CreateAsync(AdminUser.Id, new SaveLocationDto { Name = name });

    [TestMethod]
    public async Task Create_TrimsInputAndWritesAudit()
    {
        var created = await _locationService.CreateAsync(AdminUser.Id, new SaveLocationDto
        {
            Name = "  Sporthalle Nord  ",
            Description = "  ",
            Street = " Nordring 5 ",
            PostalCode = "44787",
            City = "Bochum"
        });

        Assert.AreEqual("Sporthalle Nord", created.Name);
        Assert.IsNull(created.Description);
        Assert.AreEqual("Nordring 5", created.Street);
        Assert.AreEqual(0, created.UsageCount);

        var audit = await DbContext.AuditLogs.SingleAsync(a => a.EntityType == nameof(Location));
        Assert.AreEqual(AuditAction.Create, audit.Action);
        Assert.AreEqual(created.Id, audit.EntityId);
    }

    [TestMethod]
    public async Task Create_DuplicateNameIgnoringCase_Throws()
    {
        await CreateLocationAsync("Sporthalle Nord");

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => CreateLocationAsync("sporthalle NORD"));
        StringAssert.Contains(ex.Message, "existiert bereits");
    }

    [TestMethod]
    public async Task Update_ToNameOfOtherLocation_Throws_ButSameNameIsAllowed()
    {
        var north = await CreateLocationAsync("Sporthalle Nord");
        await CreateLocationAsync("Hallenbad");

        var updated = await _locationService.UpdateAsync(AdminUser.Id, north.Id, new SaveLocationDto { Name = "Sporthalle Nord", City = "Bochum" });
        Assert.AreEqual("Bochum", updated.City);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            _locationService.UpdateAsync(AdminUser.Id, north.Id, new SaveLocationDto { Name = "HALLENBAD" }));
    }

    [TestMethod]
    public async Task Update_KeepsOldValuesInAudit()
    {
        var north = await CreateLocationAsync("Sporthalle Nord");
        await _locationService.UpdateAsync(AdminUser.Id, north.Id, new SaveLocationDto { Name = "Sporthalle Nordring" });

        var audit = await DbContext.AuditLogs.SingleAsync(a => a.EntityType == nameof(Location) && a.Action == AuditAction.Update);
        StringAssert.Contains(audit.OldValues, "Sporthalle Nord");
        StringAssert.Contains(audit.NewValues, "Sporthalle Nordring");
    }

    [TestMethod]
    public async Task Update_UnknownId_ThrowsKeyNotFound()
    {
        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(() =>
            _locationService.UpdateAsync(AdminUser.Id, Guid.NewGuid(), new SaveLocationDto { Name = "X" }));
    }

    [TestMethod]
    public async Task ClubAndDepartmentUpdate_SyncLocations_AndUsageCount()
    {
        var north = await CreateLocationAsync("Sporthalle Nord");
        var pool = await CreateLocationAsync("Hallenbad");
        var club = await SeedClubAsync();
        var department = await SeedDepartmentAsync(club.Id);

        await _clubService.UpdateAsync(AdminUser.Id, club.Id, new Club { Name = club.Name }, default, [north.Id, pool.Id]);
        await _departmentService.UpdateAsync(AdminUser.Id, department.Id, new Department { Name = department.Name, ClubId = club.Id }, default, [north.Id]);

        var all = await _locationService.GetAllAsync();
        Assert.AreEqual(2, all.Single(l => l.Id == north.Id).UsageCount);
        Assert.AreEqual(1, all.Single(l => l.Id == pool.Id).UsageCount);

        // Removing one location keeps the other; null leaves the links untouched
        await _clubService.UpdateAsync(AdminUser.Id, club.Id, new Club { Name = club.Name }, default, [pool.Id]);
        await _clubService.UpdateAsync(AdminUser.Id, club.Id, new Club { Name = club.Name }, default, null);

        DbContext.ChangeTracker.Clear();
        var reloaded = await DbContext.Clubs.Include(c => c.Locations).SingleAsync(c => c.Id == club.Id);
        CollectionAssert.AreEquivalent(new[] { pool.Id }, reloaded.Locations.Select(l => l.Id).ToArray());

        var clubAudit = await DbContext.AuditLogs
            .Where(a => a.EntityType == "Club" && a.Action == AuditAction.Update && a.NewValues != null && a.NewValues.Contains("Locations"))
            .CountAsync();
        Assert.AreEqual(2, clubAudit);
    }

    [TestMethod]
    public async Task ClubUpdate_UnknownLocation_Throws()
    {
        var club = await SeedClubAsync();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            _clubService.UpdateAsync(AdminUser.Id, club.Id, new Club { Name = club.Name }, default, [Guid.NewGuid()]));
    }

    [TestMethod]
    public async Task DeleteLocation_RemovesLinks_KeepsOrganizations()
    {
        var north = await CreateLocationAsync("Sporthalle Nord");
        var club = await SeedClubAsync();
        var department = await SeedDepartmentAsync(club.Id);
        await _clubService.UpdateAsync(AdminUser.Id, club.Id, new Club { Name = club.Name }, default, [north.Id]);
        await _departmentService.UpdateAsync(AdminUser.Id, department.Id, new Department { Name = department.Name, ClubId = club.Id }, default, [north.Id]);

        await _locationService.DeleteAsync(AdminUser.Id, north.Id);

        DbContext.ChangeTracker.Clear();
        Assert.IsFalse(await DbContext.Locations.AnyAsync());
        Assert.IsTrue(await DbContext.Clubs.AnyAsync(c => c.Id == club.Id && !c.Locations.Any()));
        Assert.IsTrue(await DbContext.Departments.AnyAsync(d => d.Id == department.Id && !d.Locations.Any()));
    }

    [TestMethod]
    public async Task DeleteClub_RemovesJoinRowsOfDepartmentsAndGroups()
    {
        var north = await CreateLocationAsync("Sporthalle Nord");
        var club = await SeedClubAsync();
        var department = await SeedDepartmentAsync(club.Id);
        await _clubService.UpdateAsync(AdminUser.Id, club.Id, new Club { Name = club.Name }, default, [north.Id]);
        await _departmentService.UpdateAsync(AdminUser.Id, department.Id, new Department { Name = department.Name, ClubId = club.Id }, default, [north.Id]);

        DbContext.ChangeTracker.Clear();
        await DbContext.Database.ExecuteSqlRawAsync("DELETE FROM Clubs WHERE Id = {0}", club.Id);

        Assert.AreEqual(0, (await _locationService.GetAllAsync()).Single().UsageCount);
    }

    [TestMethod]
    public async Task ContactPerson_ReceivesLocationInfo_IsSetOnAddAndUpdate()
    {
        var club = await SeedClubAsync();
        var user = await UserRepository.AddAsync(new User { Email = "hallenwart@test.local", FirstName = "Hanna", LastName = "Hallenwart" });

        await _clubService.AddContactPersonAsync(AdminUser.Id, club.Id, user.Id, null, false, null, receivesLocationInfo: true);
        DbContext.ChangeTracker.Clear();
        Assert.IsTrue((await DbContext.ContactAssignments.SingleAsync(a => a.UserId == user.Id)).ReceivesLocationInfo);

        await _clubService.UpdateContactPersonAsync(AdminUser.Id, club.Id, user.Id, null, false, null, receivesLocationInfo: false);
        DbContext.ChangeTracker.Clear();
        Assert.IsFalse((await DbContext.ContactAssignments.SingleAsync(a => a.UserId == user.Id)).ReceivesLocationInfo);
    }

    [TestMethod]
    public async Task RecipientCandidates_ContainOwnLocationsOnly_AndFlag()
    {
        var north = await CreateLocationAsync("Sporthalle Nord");
        var club = await SeedClubAsync();
        var department = await SeedDepartmentAsync(club.Id);
        await _clubService.UpdateAsync(AdminUser.Id, club.Id, new Club { Name = club.Name }, default, [north.Id]);

        var clubContact = await UserRepository.AddAsync(new User { Email = "verein@test.local", FirstName = "Vera", LastName = "Verein", EmailVerified = true });
        var departmentContact = await UserRepository.AddAsync(new User { Email = "abteilung@test.local", FirstName = "Anton", LastName = "Abteilung", EmailVerified = true });
        DbContext.ContactAssignments.AddRange(
            new ContactAssignment { UserId = clubContact.Id, ClubId = club.Id, ReceivesLocationInfo = true, ConfirmedAt = DateTime.UtcNow },
            new ContactAssignment { UserId = departmentContact.Id, DepartmentId = department.Id, ConfirmedAt = DateTime.UtcNow });
        await DbContext.SaveChangesAsync();

        var candidates = (await UserService.GetRecipientCandidatesAsync(AdminUser.Id)).ToList();

        var clubCandidate = candidates.Single(c => c.UserId == clubContact.Id);
        CollectionAssert.AreEqual(new[] { north.Id }, clubCandidate.LocationIds);
        Assert.IsTrue(clubCandidate.ReceivesLocationInfo);

        // No inheritance: the department does not get the club's location
        var departmentCandidate = candidates.Single(c => c.UserId == departmentContact.Id);
        Assert.AreEqual(0, departmentCandidate.LocationIds.Count);
        Assert.IsFalse(departmentCandidate.ReceivesLocationInfo);
    }
}
