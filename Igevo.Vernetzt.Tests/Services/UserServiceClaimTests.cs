using Igevo.Vernetzt.Core.Configuration;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Data;
using Igevo.Vernetzt.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Igevo.Vernetzt.Tests.TestInfrastructure;

namespace Igevo.Vernetzt.Tests.Services;

/// <summary>
/// Unit tests for UserService claim assignment and removal authorization.
/// </summary>
[TestClass]
public class UserServiceClaimTests
{
    private Mock<IUserRepository> _userRepositoryMock = null!;
    private Mock<IAuditLogRepository> _auditLogRepositoryMock = null!;
    private Mock<IClubRepository> _clubRepositoryMock = null!;
    private Mock<IDepartmentRepository> _departmentRepositoryMock = null!;
    private Mock<IContactPositionRepository> _contactPositionRepositoryMock = null!;
    private Mock<IAuthorizationService> _authorizationServiceMock = null!;
    private Mock<IEmailService> _emailServiceMock = null!;
    private Mock<IInvitationTokenRepository> _invitationTokenRepositoryMock = null!;
    private Mock<IAppSettingsService> _appSettingsServiceMock = null!;
    private Mock<ILogger<UserService>> _loggerMock = null!;
    private VernetztDbContext _dbContext = null!;
    private UserService _service = null!;

    private readonly Guid _adminUserId = Guid.NewGuid();
    private readonly Guid _managerUserId = Guid.NewGuid();
    private readonly Guid _targetUserId = Guid.NewGuid();

    [TestInitialize]
    public void Setup()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _auditLogRepositoryMock = new Mock<IAuditLogRepository>();
        _clubRepositoryMock = new Mock<IClubRepository>();
        _departmentRepositoryMock = new Mock<IDepartmentRepository>();
        _contactPositionRepositoryMock = new Mock<IContactPositionRepository>();
        _authorizationServiceMock = new Mock<IAuthorizationService>();
        _emailServiceMock = new Mock<IEmailService>();
        _invitationTokenRepositoryMock = new Mock<IInvitationTokenRepository>();
        _appSettingsServiceMock = new Mock<IAppSettingsService>();
        _loggerMock = new Mock<ILogger<UserService>>();
        _dbContext = SqliteTestDatabase.CreateContext();

        var appSettings = Options.Create(new AppSettings { SystemAdminEmail = "admin@test.local" });

        _service = new UserService(
            _userRepositoryMock.Object,
            _clubRepositoryMock.Object,
            _departmentRepositoryMock.Object,
            _contactPositionRepositoryMock.Object,
            _authorizationServiceMock.Object,
            _auditLogRepositoryMock.Object,
            _emailServiceMock.Object,
            _invitationTokenRepositoryMock.Object,
            _appSettingsServiceMock.Object,
            _dbContext,
            appSettings,
            _loggerMock.Object);

        SetupTestUsers();
    }

    private void SetupTestUsers()
    {
        var adminUser = new User
        {
            Id = _adminUserId,
            Email = "admin@test.local",
            FirstName = "Admin",
            LastName = "User",
            Claims = new List<UserClaim> { UserClaim.SystemAdmin },
        };

        var managerUser = new User
        {
            Id = _managerUserId,
            Email = "manager@test.local",
            FirstName = "Manager",
            LastName = "User",
            Claims = new List<UserClaim> { UserClaim.Manager },
        };

        var targetUser = new User
        {
            Id = _targetUserId,
            Email = "target@test.local",
            FirstName = "Target",
            LastName = "User",
            Claims = new List<UserClaim>(),
        };

        _userRepositoryMock.Setup(r => r.GetByIdAsync(_adminUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(adminUser);
        _userRepositoryMock.Setup(r => r.GetByIdAsync(_managerUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(managerUser);
        _userRepositoryMock.Setup(r => r.GetByIdAsync(_targetUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetUser);

        _userRepositoryMock.Setup(r => r.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User u, CancellationToken _) => u);

        _auditLogRepositoryMock
            .Setup(r => r.LogAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<AuditAction>(),
                It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _dbContext.Dispose();
    }

    // ─── AssignClaimAsync ────────────────────────────────────────────────────

    [TestMethod]
    public async Task AssignClaimAsync_ManagerAttemptingSystemAdminClaim_ReturnsFalse()
    {
        var result = await _service.AssignClaimAsync(_managerUserId, _targetUserId, UserClaim.SystemAdmin);

        Assert.IsFalse(result);
        _userRepositoryMock.Verify(r => r.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task AssignClaimAsync_SystemAdminAssigningSystemAdminClaim_ReturnsTrue()
    {
        var result = await _service.AssignClaimAsync(_adminUserId, _targetUserId, UserClaim.SystemAdmin);

        Assert.IsTrue(result);
        _userRepositoryMock.Verify(r => r.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task AssignClaimAsync_ManagerAssigningManagerClaim_ReturnsTrue()
    {
        var result = await _service.AssignClaimAsync(_managerUserId, _targetUserId, UserClaim.Manager);

        Assert.IsTrue(result);
        _userRepositoryMock.Verify(r => r.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task AssignClaimAsync_ManagerAssigningDataReaderClaim_ReturnsTrue()
    {
        var result = await _service.AssignClaimAsync(_managerUserId, _targetUserId, UserClaim.DataReader);

        Assert.IsTrue(result);
        _userRepositoryMock.Verify(r => r.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task AssignClaimAsync_UserWithoutPermission_ReturnsFalse()
    {
        var unprivilegedId = Guid.NewGuid();
        _userRepositoryMock.Setup(r => r.GetByIdAsync(unprivilegedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Id = unprivilegedId,
                Email = "other@test.local",
                FirstName = "Other",
                LastName = "User",
                Claims = new List<UserClaim>(),
            });

        var result = await _service.AssignClaimAsync(unprivilegedId, _targetUserId, UserClaim.DataReader);

        Assert.IsFalse(result);
        _userRepositoryMock.Verify(r => r.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── RemoveClaimAsync ────────────────────────────────────────────────────

    [TestMethod]
    public async Task RemoveClaimAsync_ManagerAttemptingRemoveSystemAdminClaim_ReturnsFalse()
    {
        var targetWithAdmin = new User
        {
            Id = _targetUserId,
            Email = "target@test.local",
            FirstName = "Target",
            LastName = "User",
            Claims = new List<UserClaim> { UserClaim.SystemAdmin },
        };
        _userRepositoryMock.Setup(r => r.GetByIdAsync(_targetUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetWithAdmin);

        var result = await _service.RemoveClaimAsync(_managerUserId, _targetUserId, UserClaim.SystemAdmin);

        Assert.IsFalse(result);
        _userRepositoryMock.Verify(r => r.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task RemoveClaimAsync_SystemAdminRemovingSystemAdminClaim_ReturnsTrue()
    {
        var targetWithAdmin = new User
        {
            Id = _targetUserId,
            Email = "target@test.local",
            FirstName = "Target",
            LastName = "User",
            Claims = new List<UserClaim> { UserClaim.SystemAdmin },
        };
        _userRepositoryMock.Setup(r => r.GetByIdAsync(_targetUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetWithAdmin);

        var result = await _service.RemoveClaimAsync(_adminUserId, _targetUserId, UserClaim.SystemAdmin);

        Assert.IsTrue(result);
        _userRepositoryMock.Verify(r => r.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task RemoveClaimAsync_ManagerRemovingManagerClaim_ReturnsTrue()
    {
        var targetWithManager = new User
        {
            Id = _targetUserId,
            Email = "target@test.local",
            FirstName = "Target",
            LastName = "User",
            Claims = new List<UserClaim> { UserClaim.Manager },
        };
        _userRepositoryMock.Setup(r => r.GetByIdAsync(_targetUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetWithManager);

        var result = await _service.RemoveClaimAsync(_managerUserId, _targetUserId, UserClaim.Manager);

        Assert.IsTrue(result);
        _userRepositoryMock.Verify(r => r.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── SystemAdmin protection / user creation / export ─────────────────────

    private User MakeTargetSystemAdmin()
    {
        var targetWithAdmin = new User
        {
            Id = _targetUserId,
            Email = "target@test.local",
            FirstName = "Target",
            LastName = "User",
            Claims = new List<UserClaim> { UserClaim.SystemAdmin },
        };
        _userRepositoryMock.Setup(r => r.GetByIdAsync(_targetUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetWithAdmin);
        _userRepositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<User> { targetWithAdmin, new() { Id = _adminUserId, Email = "admin@test.local", Claims = new List<UserClaim> { UserClaim.SystemAdmin } } });
        return targetWithAdmin;
    }

    [TestMethod]
    public async Task DeleteAsync_ManagerDeletingSystemAdmin_ReturnsFalse()
    {
        MakeTargetSystemAdmin();

        var result = await _service.DeleteAsync(_managerUserId, _targetUserId);

        Assert.IsFalse(result);
        _userRepositoryMock.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task DeleteAccountAsync_ManagerDeletingSystemAdmin_Fails()
    {
        MakeTargetSystemAdmin();

        var result = await _service.DeleteAccountAsync(_managerUserId, _targetUserId, "target@test.local");

        Assert.IsFalse(result.Success);
        _userRepositoryMock.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task CreateAsync_UserWithoutManagerOrAdminClaim_Throws()
    {
        var unprivilegedId = Guid.NewGuid();
        _userRepositoryMock.Setup(r => r.GetByIdAsync(unprivilegedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = unprivilegedId, Email = "member@test.local", Claims = new List<UserClaim>() });

        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() =>
            _service.CreateAsync(unprivilegedId, new User { Email = "new@test.local", FirstName = "Neu", LastName = "User" }));
        _userRepositoryMock.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ExportUserDataAsync_ManagerExportingOtherUser_OmitsAuditLog()
    {
        _auditLogRepositoryMock.Setup(r => r.GetByUserAsync(_targetUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuditLog> { new() { Action = AuditAction.LoginAttemptFailed, EntityType = "AuthLogin", NewValues = "{\"IpAddress\":\"10.0.0.1\"}" } });

        var export = await _service.ExportUserDataAsync(_managerUserId, _targetUserId);

        Assert.AreEqual(0, export.AuditLog.Count);
    }

    [TestMethod]
    public async Task ExportUserDataAsync_SystemAdminExportingOtherUser_IncludesAuditLog()
    {
        _auditLogRepositoryMock.Setup(r => r.GetByUserAsync(_targetUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuditLog> { new() { Action = AuditAction.LoginAttemptFailed, EntityType = "AuthLogin" } });

        var export = await _service.ExportUserDataAsync(_adminUserId, _targetUserId);

        Assert.AreEqual(1, export.AuditLog.Count);
    }

    // ─── UpdateAsync ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task UpdateAsync_ManagerUpdatingSystemAdmin_ReturnsNull()
    {
        var result = await _service.UpdateAsync(_managerUserId, _adminUserId, new User { FirstName = "X", LastName = "Y" });

        Assert.IsNull(result);
        _userRepositoryMock.Verify(r => r.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task UpdateAsync_ManagerUpdatingRegularUser_Succeeds()
    {
        var result = await _service.UpdateAsync(_managerUserId, _targetUserId, new User { FirstName = "X", LastName = "Y" });

        Assert.IsNotNull(result);
    }
}
