using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;

namespace Igevo.Vernetzt.Tests.Services;

/// <summary>
/// Unit tests for AuthorizationService covering visibility and access control.
/// </summary>
[TestClass]
public class AuthorizationServiceTests
{
    private Mock<IUserRepository> _userRepositoryMock = null!;
    private Mock<IClubRepository> _clubRepositoryMock = null!;
    private Mock<IDepartmentRepository> _departmentRepositoryMock = null!;
    private Mock<IHttpContextAccessor> _httpContextAccessorMock = null!;
    private Mock<ILogger<AuthorizationService>> _loggerMock = null!;
    private AuthorizationService _service = null!;

    private readonly Guid _adminUserId = Guid.NewGuid();
    private readonly Guid _managerUserId = Guid.NewGuid();
    private readonly Guid _dataReaderUserId = Guid.NewGuid();
    private readonly Guid _regularUserId = Guid.NewGuid();
    private readonly Guid _clubId = Guid.NewGuid();
    private readonly Guid _departmentId = Guid.NewGuid();

    [TestInitialize]
    public void Setup()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _clubRepositoryMock = new Mock<IClubRepository>();
        _departmentRepositoryMock = new Mock<IDepartmentRepository>();
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        _loggerMock = new Mock<ILogger<AuthorizationService>>();

        _service = new AuthorizationService(
            _userRepositoryMock.Object,
            _clubRepositoryMock.Object,
            _departmentRepositoryMock.Object,
            _httpContextAccessorMock.Object,
            _loggerMock.Object);

        SetupTestUsers();
        SetupTestClubAndDepartment();
    }

    private void SetupTestUsers()
    {
        // System Admin user
        var adminUser = new User
        {
            Id = _adminUserId,
            Email = "admin@test.com",
            FirstName = "Admin",
            LastName = "User",
            Claims = new List<UserClaim> { UserClaim.SystemAdmin },
        };

        // Manager user
        var managerUser = new User
        {
            Id = _managerUserId,
            Email = "manager@test.com",
            FirstName = "Manager",
            LastName = "User",
            Claims = new List<UserClaim> { UserClaim.Manager },
        };

        // Data Reader user
        var dataReaderUser = new User
        {
            Id = _dataReaderUserId,
            Email = "reader@test.com",
            FirstName = "Reader",
            LastName = "User",
            Claims = new List<UserClaim> { UserClaim.DataReader },
        };

        // Regular user (no special claims)
        var regularUser = new User
        {
            Id = _regularUserId,
            Email = "regular@test.com",
            FirstName = "Regular",
            LastName = "User",
            Claims = new List<UserClaim>(),
        };

        _userRepositoryMock.Setup(r => r.GetByIdAsync(_adminUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(adminUser);
        _userRepositoryMock.Setup(r => r.GetByIdAsync(_managerUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(managerUser);
        _userRepositoryMock.Setup(r => r.GetByIdAsync(_dataReaderUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dataReaderUser);
        _userRepositoryMock.Setup(r => r.GetByIdAsync(_regularUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(regularUser);
    }

    private void SetupTestClubAndDepartment()
    {
        var club = new Club
        {
            Id = _clubId,
            Name = "Test Club",
            ContactAssignments = new List<ContactAssignment>
            {
                new() { UserId = _regularUserId, ClubId = _clubId, AssignedAt = DateTime.UtcNow, CanEdit = true }
            }
        };

        var department = new Department
        {
            Id = _departmentId,
            Name = "Test Department",
            ClubId = _clubId,
            ContactAssignments = new List<ContactAssignment>
            {
                new() { UserId = _regularUserId, DepartmentId = _departmentId, AssignedAt = DateTime.UtcNow, CanEdit = true }
            }
        };

        _clubRepositoryMock.Setup(r => r.GetByIdAsync(_clubId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(club);
        _clubRepositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Club> { club });

        _departmentRepositoryMock.Setup(r => r.GetByIdAsync(_departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(department);
        _departmentRepositoryMock.Setup(r => r.GetByClubIdAsync(_clubId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Department> { department });
    }

    #region IsSystemAdminAsync Tests

    [TestMethod]
    public async Task IsSystemAdminAsync_AdminUser_ReturnsTrue()
    {
        // Act
        var result = await _service.IsSystemAdminAsync(_adminUserId);

        // Assert
        Assert.IsTrue(result);
    }

    [TestMethod]
    public async Task IsSystemAdminAsync_ManagerUser_ReturnsFalse()
    {
        // Act
        var result = await _service.IsSystemAdminAsync(_managerUserId);

        // Assert
        Assert.IsFalse(result);
    }

    [TestMethod]
    public async Task IsSystemAdminAsync_RegularUser_ReturnsFalse()
    {
        // Act
        var result = await _service.IsSystemAdminAsync(_regularUserId);

        // Assert
        Assert.IsFalse(result);
    }

    [TestMethod]
    public async Task IsSystemAdminAsync_NonExistentUser_ReturnsFalse()
    {
        // Arrange
        var nonExistentUserId = Guid.NewGuid();
        _userRepositoryMock.Setup(r => r.GetByIdAsync(nonExistentUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _service.IsSystemAdminAsync(nonExistentUserId);

        // Assert
        Assert.IsFalse(result);
    }

    #endregion

    #region IsSystemAdminAsync Tests (for specific user)

    [TestMethod]
    public async Task IsSystemAdminAsync_WithUserId_AdminUser_ReturnsTrue()
    {
        // Act
        var result = await _service.IsSystemAdminAsync(_adminUserId);

        // Assert
        Assert.IsTrue(result);
    }

    [TestMethod]
    public async Task IsSystemAdminAsync_WithUserId_RegularUser_ReturnsFalse()
    {
        // Act
        var result = await _service.IsSystemAdminAsync(_regularUserId);

        // Assert
        Assert.IsFalse(result);
    }

    [TestMethod]
    public async Task IsSystemAdminAsync_WithUserId_ManagerUser_ReturnsFalse()
    {
        // Manager is not SystemAdmin
        // Act
        var result = await _service.IsSystemAdminAsync(_managerUserId);

        // Assert
        Assert.IsFalse(result);
    }

    #endregion

    #region CanEditClubAsync Tests

    [TestMethod]
    public async Task CanEditClubAsync_AdminUser_ReturnsTrue()
    {
        // Act
        var result = await _service.CanEditClubAsync(_adminUserId, _clubId);

        // Assert
        Assert.IsTrue(result);
    }

    [TestMethod]
    public async Task CanEditClubAsync_ManagerUser_ReturnsTrue()
    {
        // Act
        var result = await _service.CanEditClubAsync(_managerUserId, _clubId);

        // Assert
        Assert.IsTrue(result);
    }

    [TestMethod]
    public async Task CanEditClubAsync_ContactPerson_ReturnsTrue()
    {
        // Regular user is assigned as contact person to the club
        // Act
        var result = await _service.CanEditClubAsync(_regularUserId, _clubId);

        // Assert
        Assert.IsTrue(result);
    }

    [TestMethod]
    public async Task CanEditClubAsync_DataReaderNotAssigned_ReturnsFalse()
    {
        // Data reader user is not assigned to the club
        // Act
        var result = await _service.CanEditClubAsync(_dataReaderUserId, _clubId);

        // Assert
        Assert.IsFalse(result);
    }

    [TestMethod]
    public async Task CanEditClubAsync_NonExistentClub_ReturnsFalse()
    {
        // Arrange - Use regular user since admin can edit anything
        var nonExistentClubId = Guid.NewGuid();
        _clubRepositoryMock.Setup(r => r.GetByIdAsync(nonExistentClubId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Club?)null);

        // Act
        var result = await _service.CanEditClubAsync(_regularUserId, nonExistentClubId);

        // Assert
        Assert.IsFalse(result);
    }

    #endregion

    #region CanEditDepartmentAsync Tests

    [TestMethod]
    public async Task CanEditDepartmentAsync_AdminUser_ReturnsTrue()
    {
        // Act
        var result = await _service.CanEditDepartmentAsync(_adminUserId, _departmentId);

        // Assert
        Assert.IsTrue(result);
    }

    [TestMethod]
    public async Task CanEditDepartmentAsync_ManagerUser_ReturnsTrue()
    {
        // Act
        var result = await _service.CanEditDepartmentAsync(_managerUserId, _departmentId);

        // Assert
        Assert.IsTrue(result);
    }

    [TestMethod]
    public async Task CanEditDepartmentAsync_DepartmentContactPerson_ReturnsTrue()
    {
        // Regular user is assigned to the department
        // Act
        var result = await _service.CanEditDepartmentAsync(_regularUserId, _departmentId);

        // Assert
        Assert.IsTrue(result);
    }

    [TestMethod]
    public async Task CanEditDepartmentAsync_NonExistentDepartment_ReturnsFalse()
    {
        // Arrange - Use regular user since admin can edit anything
        var nonExistentDeptId = Guid.NewGuid();
        _departmentRepositoryMock.Setup(r => r.GetByIdAsync(nonExistentDeptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Department?)null);

        // Act
        var result = await _service.CanEditDepartmentAsync(_regularUserId, nonExistentDeptId);

        // Assert
        Assert.IsFalse(result);
    }

    #endregion

    #region Group hierarchy (club > department > group)

    /// <summary>
    /// Builds club > department > group with one editor per level and no other assignments.
    /// </summary>
    private (Guid DepartmentEditor, Guid GroupEditor, Guid ClubEditor, Guid DepartmentId, Guid GroupId, Guid SiblingGroupId) SetupGroupHierarchy()
    {
        var clubId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var siblingGroupId = Guid.NewGuid();
        var departmentEditor = Guid.NewGuid();
        var groupEditor = Guid.NewGuid();
        var clubEditor = Guid.NewGuid();

        foreach (var userId in new[] { departmentEditor, groupEditor, clubEditor })
        {
            _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new User { Id = userId, Email = $"{userId}@test.com", Claims = [] });
        }

        var club = new Club
        {
            Id = clubId,
            Name = "TSV",
            ContactAssignments = [new() { UserId = clubEditor, ClubId = clubId, CanEdit = true }]
        };
        var department = new Department
        {
            Id = departmentId,
            Name = "Handball",
            ClubId = clubId,
            ContactAssignments = [new() { UserId = departmentEditor, DepartmentId = departmentId, CanEdit = true }]
        };
        var group = new Department
        {
            Id = groupId,
            Name = "Bambini",
            ClubId = clubId,
            ParentDepartmentId = departmentId,
            ParentDepartment = department,
            ContactAssignments = [new() { UserId = groupEditor, DepartmentId = groupId, CanEdit = true }]
        };
        var sibling = new Department
        {
            Id = siblingGroupId,
            Name = "Alte Herren",
            ClubId = clubId,
            ParentDepartmentId = departmentId,
            ParentDepartment = department
        };

        _clubRepositoryMock.Setup(r => r.GetByIdAsync(clubId, It.IsAny<CancellationToken>())).ReturnsAsync(club);
        _departmentRepositoryMock.Setup(r => r.GetByIdAsync(departmentId, It.IsAny<CancellationToken>())).ReturnsAsync(department);
        _departmentRepositoryMock.Setup(r => r.GetByIdAsync(groupId, It.IsAny<CancellationToken>())).ReturnsAsync(group);
        _departmentRepositoryMock.Setup(r => r.GetByIdAsync(siblingGroupId, It.IsAny<CancellationToken>())).ReturnsAsync(sibling);

        return (departmentEditor, groupEditor, clubEditor, departmentId, groupId, siblingGroupId);
    }

    [TestMethod]
    public async Task CanEditDepartmentAsync_GroupContactPersonWithEditRights_CanEditOwnGroup()
    {
        var h = SetupGroupHierarchy();

        Assert.IsTrue(await _service.CanEditDepartmentAsync(h.GroupEditor, h.GroupId));
    }

    [TestMethod]
    public async Task CanEditDepartmentAsync_GroupEditor_CannotEditParentDepartment()
    {
        var h = SetupGroupHierarchy();

        Assert.IsFalse(await _service.CanEditDepartmentAsync(h.GroupEditor, h.DepartmentId));
    }

    [TestMethod]
    public async Task CanEditDepartmentAsync_GroupEditor_CannotEditSiblingGroup()
    {
        var h = SetupGroupHierarchy();

        Assert.IsFalse(await _service.CanEditDepartmentAsync(h.GroupEditor, h.SiblingGroupId));
    }

    [TestMethod]
    public async Task CanEditDepartmentAsync_DepartmentEditor_CanEditAllGroupsBelow()
    {
        var h = SetupGroupHierarchy();

        Assert.IsTrue(await _service.CanEditDepartmentAsync(h.DepartmentEditor, h.GroupId));
        Assert.IsTrue(await _service.CanEditDepartmentAsync(h.DepartmentEditor, h.SiblingGroupId));
    }

    [TestMethod]
    public async Task CanEditDepartmentAsync_ClubEditor_CanEditGroups()
    {
        var h = SetupGroupHierarchy();

        Assert.IsTrue(await _service.CanEditDepartmentAsync(h.ClubEditor, h.GroupId));
    }

    #endregion
}
