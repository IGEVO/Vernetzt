using Igevo.Vernetzt.Core.DTOs.Common;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Igevo.Vernetzt.Tests.Services;

[TestClass]
public class DashboardServiceTests
{
    private Mock<IClubRepository> _clubRepo = null!;
    private Mock<IDepartmentRepository> _departmentRepo = null!;
    private Mock<IUserRepository> _userRepo = null!;
    private DashboardService _service = null!;

    private readonly Guid _adminUserId = Guid.NewGuid();
    private readonly Guid _managerUserId = Guid.NewGuid();
    private readonly Guid _dataReaderUserId = Guid.NewGuid();
    private readonly Guid _regularUserId = Guid.NewGuid();
    private readonly Guid _unassignedUserId = Guid.NewGuid();

    private User _adminUser = null!;
    private User _managerUser = null!;
    private User _dataReaderUser = null!;
    private User _regularUser = null!;
    private User _unassignedUser = null!;

    private Club _clubA = null!;
    private Club _clubB = null!;
    private Department _deptA1 = null!;
    private Department _deptA2 = null!;
    private Department _deptB1 = null!;

    [TestInitialize]
    public void Setup()
    {
        _clubRepo = new Mock<IClubRepository>();
        _departmentRepo = new Mock<IDepartmentRepository>();
        _userRepo = new Mock<IUserRepository>();

        _service = new DashboardService(
            _clubRepo.Object,
            _departmentRepo.Object,
            _userRepo.Object,
            new Mock<ILogger<DashboardService>>().Object);

        // Setup users
        _adminUser = new User
        {
            Id = _adminUserId,
            Email = "admin@test.local",
            FirstName = "Admin",
            LastName = "User",
            Claims = [UserClaim.SystemAdmin]
        };
        _managerUser = new User
        {
            Id = _managerUserId,
            Email = "manager@test.local",
            FirstName = "Manager",
            LastName = "User",
            Claims = [UserClaim.Manager]
        };
        _dataReaderUser = new User
        {
            Id = _dataReaderUserId,
            Email = "reader@test.local",
            FirstName = "Reader",
            LastName = "User",
            Claims = [UserClaim.DataReader]
        };
        _regularUser = new User
        {
            Id = _regularUserId,
            Email = "regular@test.local",
            FirstName = "Regular",
            LastName = "User",
            Claims = []
        };
        _unassignedUser = new User
        {
            Id = _unassignedUserId,
            Email = "unassigned@test.local",
            FirstName = "Unassigned",
            LastName = "User",
            Claims = []
        };

        var allUsers = new List<User> { _adminUser, _managerUser, _dataReaderUser, _regularUser, _unassignedUser };

        // Setup clubs with contact assignments
        _clubA = new Club
        {
            Id = Guid.NewGuid(),
            Name = "FC Test",
            City = "Teststadt",
            ContactAssignments = new List<ContactAssignment>
            {
                new() { UserId = _regularUserId, ClubId = default, AssignedAt = DateTime.UtcNow }
            }
        };
        _clubA.ContactAssignments.First().ClubId = _clubA.Id;

        _clubB = new Club
        {
            Id = Guid.NewGuid(),
            Name = "TV Beispiel",
            City = "Musterstadt",
            ContactAssignments = new List<ContactAssignment>()
        };

        // Setup departments
        _deptA1 = new Department
        {
            Id = Guid.NewGuid(),
            Name = "Fußball",
            ClubId = _clubA.Id,
            ContactAssignments = new List<ContactAssignment>
            {
                new() { UserId = _regularUserId, DepartmentId = default, AssignedAt = DateTime.UtcNow }
            }
        };
        _deptA1.ContactAssignments.First().DepartmentId = _deptA1.Id;

        _deptA2 = new Department
        {
            Id = Guid.NewGuid(),
            Name = "Handball",
            ClubId = _clubA.Id,
            ContactAssignments = new List<ContactAssignment>()
        };

        _deptB1 = new Department
        {
            Id = Guid.NewGuid(),
            Name = "Tennis",
            ClubId = _clubB.Id,
            ContactAssignments = new List<ContactAssignment>
            {
                new() { UserId = _regularUserId, DepartmentId = default, AssignedAt = DateTime.UtcNow }
            }
        };
        _deptB1.ContactAssignments.First().DepartmentId = _deptB1.Id;

        var allClubs = new List<Club> { _clubA, _clubB };
        var allDepartments = new List<Department> { _deptA1, _deptA2, _deptB1 };

        // Wire up repository mocks
        _clubRepo.Setup(r => r.GetAllWithDetailsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(allClubs);
        _departmentRepo.Setup(r => r.GetAllWithDetailsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(allDepartments);

        // Wire up user lookups
        foreach (var user in allUsers)
        {
            _userRepo.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        }
    }

    [TestMethod]
    public async Task GetDashboardData_ReturnsUserDisplayName()
    {
        var result = await _service.GetDashboardDataAsync(_regularUserId);

        Assert.AreEqual("Regular User", result.UserDisplayName);
    }

    [TestMethod]
    public async Task GetDashboardData_UnknownUser_ReturnsEmptyDisplayName()
    {
        _userRepo.Setup(r => r.GetByIdAsync(It.Is<Guid>(id => id == Guid.Empty), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var result = await _service.GetDashboardDataAsync(Guid.Empty);

        Assert.AreEqual(string.Empty, result.UserDisplayName);
    }

    [TestMethod]
    public async Task GetDashboardData_UnknownUser_ReturnsEmptyMyClubs()
    {
        _userRepo.Setup(r => r.GetByIdAsync(It.Is<Guid>(id => id == Guid.Empty), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var result = await _service.GetDashboardDataAsync(Guid.Empty);

        Assert.AreEqual(0, result.MyClubs.Count);
    }

    [TestMethod]
    public async Task GetDashboardData_SystemAdmin_WithoutAssignment_SeesNoClubs()
    {
        // Admin has no ContactAssignments → should see no clubs in "My Clubs"
        var result = await _service.GetDashboardDataAsync(_adminUserId);

        Assert.AreEqual(0, result.MyClubs.Count);
    }

    [TestMethod]
    public async Task GetDashboardData_SystemAdmin_WithAssignment_SeesOnlyAssignedClub()
    {
        // Add admin as contact person for Club B
        _clubB.ContactAssignments = new List<ContactAssignment>
        {
            new() { UserId = _adminUserId, ClubId = _clubB.Id, AssignedAt = DateTime.UtcNow }
        };

        var result = await _service.GetDashboardDataAsync(_adminUserId);

        Assert.AreEqual(1, result.MyClubs.Count);
        Assert.AreEqual(_clubB.Id, result.MyClubs[0].Id);
    }

    [TestMethod]
    public async Task GetDashboardData_Manager_WithoutAssignment_SeesNoClubs()
    {
        var result = await _service.GetDashboardDataAsync(_managerUserId);

        Assert.AreEqual(0, result.MyClubs.Count);
    }

    [TestMethod]
    public async Task GetDashboardData_Manager_WithAssignment_SeesOnlyAssignedClub()
    {
        // Add manager as contact person for Club A
        _clubA.ContactAssignments.Add(
            new ContactAssignment { UserId = _managerUserId, ClubId = _clubA.Id, AssignedAt = DateTime.UtcNow });

        var result = await _service.GetDashboardDataAsync(_managerUserId);

        Assert.AreEqual(1, result.MyClubs.Count);
        Assert.AreEqual(_clubA.Id, result.MyClubs[0].Id);
    }

    [TestMethod]
    public async Task GetDashboardData_DataReader_HasNoClubsInMyClubs()
    {
        var result = await _service.GetDashboardDataAsync(_dataReaderUserId);

        Assert.AreEqual(0, result.MyClubs.Count);
    }

    [TestMethod]
    public async Task GetDashboardData_RegularUser_SeesOnlyAssignedClubs()
    {
        // _regularUser is contact person for _clubA (direct) and _deptB1 (via department)
        var result = await _service.GetDashboardDataAsync(_regularUserId);

        // Club A: direct contact person
        // Club B: via department B1 contact assignment
        Assert.AreEqual(2, result.MyClubs.Count);

        var clubA = result.MyClubs.Single(c => c.Id == _clubA.Id);
        Assert.IsTrue(clubA.IsContactPerson);
        Assert.IsTrue(clubA.CanEdit);

        var clubB = result.MyClubs.Single(c => c.Id == _clubB.Id);
        Assert.IsFalse(clubB.IsContactPerson);
        Assert.IsFalse(clubB.CanEdit); // Not a club-level contact person
    }

    [TestMethod]
    public async Task GetDashboardData_RegularUser_ClubContactSeesOnlyAssignedDepartments()
    {
        // User is club contact for Club A but only department contact for deptA1 → sees only deptA1
        var result = await _service.GetDashboardDataAsync(_regularUserId);

        var clubA = result.MyClubs.Single(c => c.Id == _clubA.Id);
        Assert.AreEqual(1, clubA.Departments.Count);
        Assert.AreEqual(_deptA1.Id, clubA.Departments[0].Id);
    }

    [TestMethod]
    public async Task GetDashboardData_RegularUser_DeptContactSeesOnlyAssignedDepartment()
    {
        // User is only department contact in Club B → only sees their department
        var result = await _service.GetDashboardDataAsync(_regularUserId);

        var clubB = result.MyClubs.Single(c => c.Id == _clubB.Id);
        Assert.AreEqual(1, clubB.Departments.Count);
        Assert.AreEqual(_deptB1.Id, clubB.Departments[0].Id);
        Assert.IsTrue(clubB.Departments[0].IsContactPerson);
        Assert.IsTrue(clubB.Departments[0].CanEdit);
    }

    [TestMethod]
    public async Task GetDashboardData_UnassignedUser_HasNoClubs()
    {
        var result = await _service.GetDashboardDataAsync(_unassignedUserId);

        Assert.AreEqual(0, result.MyClubs.Count);
    }

    [TestMethod]
    public async Task GetDashboardData_ClubContactWithNoDepartmentAssignment_SeesNoDepartments()
    {
        // Create a user who is club contact for Club B but not assigned to any department
        var clubOnlyUserId = Guid.NewGuid();
        var clubOnlyUser = new User
        {
            Id = clubOnlyUserId,
            Email = "clubonly@test.local",
            FirstName = "ClubOnly",
            LastName = "User",
            Claims = []
        };
        _userRepo.Setup(r => r.GetByIdAsync(clubOnlyUserId, It.IsAny<CancellationToken>())).ReturnsAsync(clubOnlyUser);
        _clubB.ContactAssignments = new List<ContactAssignment>
        {
            new() { UserId = clubOnlyUserId, ClubId = _clubB.Id, AssignedAt = DateTime.UtcNow }
        };

        var result = await _service.GetDashboardDataAsync(clubOnlyUserId);

        Assert.AreEqual(1, result.MyClubs.Count);
        var clubB = result.MyClubs.Single(c => c.Id == _clubB.Id);
        Assert.AreEqual(0, clubB.Departments.Count);
    }

    [TestMethod]
    public async Task GetDashboardData_MyClubs_AreSortedByName()
    {
        var result = await _service.GetDashboardDataAsync(_regularUserId);

        var names = result.MyClubs.Select(c => c.Name).ToList();
        var sorted = names.OrderBy(n => n).ToList();
        CollectionAssert.AreEqual(sorted, names);
    }

    [TestMethod]
    public async Task GetDashboardData_Departments_AreSortedByName()
    {
        var result = await _service.GetDashboardDataAsync(_regularUserId);

        var clubA = result.MyClubs.Single(c => c.Id == _clubA.Id);
        var deptNames = clubA.Departments.Select(d => d.Name).ToList();
        var sorted = deptNames.OrderBy(n => n).ToList();
        CollectionAssert.AreEqual(sorted, deptNames);
    }
}
