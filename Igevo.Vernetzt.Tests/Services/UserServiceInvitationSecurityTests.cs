using Igevo.Vernetzt.Core.Configuration;
using Igevo.Vernetzt.Core.DTOs.Invitations;
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
/// Security tests for invitation claim validation in UserService.
/// Covers privilege escalation prevention during invite and acceptance flows.
/// </summary>
[TestClass]
public class UserServiceInvitationSecurityTests
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
    private readonly Guid _unprivilegedUserId = Guid.NewGuid();

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

        var appSettings = Options.Create(new AppSettings
        {
            SystemAdminEmail = "admin@test.local",
            InvitationTokenExpirationDays = 7
        });

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

        SetupUsers();
        SetupMandatorySettings();
    }

    private void SetupUsers()
    {
        var adminUser = new User
        {
            Id = _adminUserId,
            Email = "admin@test.local",
            FirstName = "Admin",
            LastName = "User",
            Claims = new List<UserClaim> { UserClaim.SystemAdmin, UserClaim.Manager },
        };

        var managerUser = new User
        {
            Id = _managerUserId,
            Email = "manager@test.local",
            FirstName = "Manager",
            LastName = "User",
            Claims = new List<UserClaim> { UserClaim.Manager },
        };

        var unprivilegedUser = new User
        {
            Id = _unprivilegedUserId,
            Email = "user@test.local",
            FirstName = "Normal",
            LastName = "User",
            Claims = new List<UserClaim> { UserClaim.DataReader },
        };

        _userRepositoryMock.Setup(r => r.GetByIdAsync(_adminUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(adminUser);
        _userRepositoryMock.Setup(r => r.GetByIdAsync(_managerUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(managerUser);
        _userRepositoryMock.Setup(r => r.GetByIdAsync(_unprivilegedUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(unprivilegedUser);

        _userRepositoryMock.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        _userRepositoryMock.Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User u, CancellationToken _) => u);
        _userRepositoryMock.Setup(r => r.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User u, CancellationToken _) => u);
    }

    private void SetupMandatorySettings()
    {
        _appSettingsServiceMock
            .Setup(s => s.GetValuesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>
            {
                { "PrivacyPolicyUrl", "https://example.com/privacy" },
                { "ContactName", "Test Admin" },
                { "ContactEmail", "contact@test.local" }
            });

        _invitationTokenRepositoryMock
            .Setup(r => r.AddAsync(It.IsAny<InvitationToken>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InvitationToken t, CancellationToken _) => t);

        _emailServiceMock
            .Setup(s => s.SendInvitationEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _auditLogRepositoryMock
            .Setup(r => r.LogAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    // ─── InviteUserAsync: Claim Validation ──────────────────────────────────

    [TestMethod]
    public async Task InviteUserAsync_AdminGrantingManager_Succeeds()
    {
        // Admin has only SystemAdmin claim (not Manager) — the specific reported bug scenario
        var pureAdminId = Guid.NewGuid();
        _userRepositoryMock.Setup(r => r.GetByIdAsync(pureAdminId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Id = pureAdminId,
                Email = "pureadmin@test.local",
                FirstName = "Pure",
                LastName = "Admin",
                Claims = new List<UserClaim> { UserClaim.SystemAdmin }
            });

        var dto = new InviteUserDto
        {
            Email = "new@test.local",
            PendingClaims = new List<UserClaim> { UserClaim.Manager }
        };

        var result = await _service.InviteUserAsync(pureAdminId, dto);

        Assert.IsTrue(result.Success);
        _invitationTokenRepositoryMock.Verify(
            r => r.AddAsync(It.IsAny<InvitationToken>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task InviteUserAsync_ManagerGrantingSystemAdmin_ReturnsFailure()
    {
        var dto = new InviteUserDto
        {
            Email = "new@test.local",
            PendingClaims = new List<UserClaim> { UserClaim.SystemAdmin }
        };

        var result = await _service.InviteUserAsync(_managerUserId, dto);

        Assert.IsFalse(result.Success);
        _invitationTokenRepositoryMock.Verify(
            r => r.AddAsync(It.IsAny<InvitationToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task InviteUserAsync_UnprivilegedUserGrantingManager_ReturnsFailure()
    {
        var dto = new InviteUserDto
        {
            Email = "new@test.local",
            PendingClaims = new List<UserClaim> { UserClaim.Manager }
        };

        var result = await _service.InviteUserAsync(_unprivilegedUserId, dto);

        Assert.IsFalse(result.Success);
        _invitationTokenRepositoryMock.Verify(
            r => r.AddAsync(It.IsAny<InvitationToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task InviteUserAsync_AdminGrantingSystemAdmin_Succeeds()
    {
        var dto = new InviteUserDto
        {
            Email = "new@test.local",
            PendingClaims = new List<UserClaim> { UserClaim.SystemAdmin }
        };

        var result = await _service.InviteUserAsync(_adminUserId, dto);

        Assert.IsTrue(result.Success);
        _invitationTokenRepositoryMock.Verify(
            r => r.AddAsync(It.IsAny<InvitationToken>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task InviteUserAsync_ManagerGrantingManager_Succeeds()
    {
        var dto = new InviteUserDto
        {
            Email = "new@test.local",
            PendingClaims = new List<UserClaim> { UserClaim.Manager }
        };

        var result = await _service.InviteUserAsync(_managerUserId, dto);

        Assert.IsTrue(result.Success);
        _invitationTokenRepositoryMock.Verify(
            r => r.AddAsync(It.IsAny<InvitationToken>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task InviteUserAsync_WithoutScope_ByUnprivilegedUser_ReturnsFailure()
    {
        var result = await _service.InviteUserAsync(_unprivilegedUserId, new InviteUserDto { Email = "new@test.local" });

        Assert.IsFalse(result.Success);
        _invitationTokenRepositoryMock.Verify(r => r.AddAsync(It.IsAny<InvitationToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task InviteUserAsync_IntoClubWithoutEditRights_WithCanEdit_ReturnsFailure()
    {
        var clubId = Guid.NewGuid();
        _clubRepositoryMock.Setup(r => r.GetByIdAsync(clubId, It.IsAny<CancellationToken>())).ReturnsAsync(new Club { Id = clubId, Name = "Fremder Verein" });
        _authorizationServiceMock.Setup(a => a.CanEditClubAsync(_unprivilegedUserId, clubId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await _service.InviteUserAsync(_unprivilegedUserId, new InviteUserDto { Email = "new@test.local", ClubId = clubId, CanEdit = true });

        Assert.IsFalse(result.Success);
        _invitationTokenRepositoryMock.Verify(r => r.AddAsync(It.IsAny<InvitationToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task InviteUserAsync_IntoDepartmentWithoutEditRights_ReturnsFailure()
    {
        var departmentId = Guid.NewGuid();
        _departmentRepositoryMock.Setup(r => r.GetByIdAsync(departmentId, It.IsAny<CancellationToken>())).ReturnsAsync(new Department { Id = departmentId, Name = "Fremde Abteilung" });
        _authorizationServiceMock.Setup(a => a.CanEditDepartmentAsync(_unprivilegedUserId, departmentId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await _service.InviteUserAsync(_unprivilegedUserId, new InviteUserDto { Email = "new@test.local", DepartmentId = departmentId, CanEdit = true });

        Assert.IsFalse(result.Success);
    }

    [TestMethod]
    public async Task InviteUserAsync_IntoOwnClub_ByClubEditor_Succeeds()
    {
        var clubId = Guid.NewGuid();
        _clubRepositoryMock.Setup(r => r.GetByIdAsync(clubId, It.IsAny<CancellationToken>())).ReturnsAsync(new Club { Id = clubId, Name = "Eigener Verein" });
        _authorizationServiceMock.Setup(a => a.CanEditClubAsync(_unprivilegedUserId, clubId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _service.InviteUserAsync(_unprivilegedUserId, new InviteUserDto { Email = "new@test.local", ClubId = clubId, CanEdit = true });

        Assert.IsTrue(result.Success);
    }

    [TestMethod]
    public async Task InviteUserAsync_NoPendingClaims_Succeeds()
    {
        var dto = new InviteUserDto
        {
            Email = "new@test.local",
            PendingClaims = null
        };

        var result = await _service.InviteUserAsync(_managerUserId, dto);

        Assert.IsTrue(result.Success);
    }

    [TestMethod]
    public async Task InviteUserAsync_EmptyPendingClaims_Succeeds()
    {
        var dto = new InviteUserDto
        {
            Email = "new@test.local",
            PendingClaims = new List<UserClaim>()
        };

        var result = await _service.InviteUserAsync(_managerUserId, dto);

        Assert.IsTrue(result.Success);
    }

    [TestMethod]
    public async Task InviteUserAsync_MixedAuthorizedAndUnauthorized_ReturnsFailure()
    {
        var dto = new InviteUserDto
        {
            Email = "new@test.local",
            PendingClaims = new List<UserClaim> { UserClaim.Manager, UserClaim.SystemAdmin }
        };

        var result = await _service.InviteUserAsync(_managerUserId, dto);

        Assert.IsFalse(result.Success);
        _invitationTokenRepositoryMock.Verify(
            r => r.AddAsync(It.IsAny<InvitationToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── AcceptInvitationAsync: Defense-in-Depth Claim Filtering ────────────

    [TestMethod]
    public async Task AcceptInvitationAsync_FiltersUnauthorizedClaimsFromInviter()
    {
        var inviterId = _managerUserId;
        var invitation = CreateValidInvitation(inviterId, "new@test.local",
            pendingClaims: new List<UserClaim> { UserClaim.SystemAdmin, UserClaim.Manager });

        SetupInvitationMocks(invitation);

        var dto = new AcceptInvitationDto
        {
            Token = invitation.Token,
            FirstName = "New",
            LastName = "User",
            AcceptedPrivacyPolicy = true
        };

        var result = await _service.AcceptInvitationAsync(dto);

        Assert.IsTrue(result.Success);

        // Only Manager should be applied (inviter has Manager but not SystemAdmin)
        _userRepositoryMock.Verify(r => r.UpdateAsync(
            It.Is<User>(u => u.Claims.Contains(UserClaim.Manager) && !u.Claims.Contains(UserClaim.SystemAdmin)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task AcceptInvitationAsync_AllClaimsUnauthorized_NoClaimsApplied()
    {
        var inviterId = _unprivilegedUserId; // only has DataReader
        var invitation = CreateValidInvitation(inviterId, "new@test.local",
            pendingClaims: new List<UserClaim> { UserClaim.SystemAdmin, UserClaim.Manager });

        SetupInvitationMocks(invitation);

        var dto = new AcceptInvitationDto
        {
            Token = invitation.Token,
            FirstName = "New",
            LastName = "User",
            AcceptedPrivacyPolicy = true
        };

        var result = await _service.AcceptInvitationAsync(dto);

        Assert.IsTrue(result.Success);

        // User.UpdateAsync should only be called for AddAsync (initial creation), not for claims
        // The user should not get SystemAdmin or Manager claims
        _userRepositoryMock.Verify(r => r.UpdateAsync(
            It.Is<User>(u => u.Claims.Contains(UserClaim.SystemAdmin) || u.Claims.Contains(UserClaim.Manager)),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task AcceptInvitationAsync_AllClaimsAuthorized_AllApplied()
    {
        var inviterId = _adminUserId; // has SystemAdmin + Manager
        var invitation = CreateValidInvitation(inviterId, "new@test.local",
            pendingClaims: new List<UserClaim> { UserClaim.SystemAdmin, UserClaim.Manager });

        SetupInvitationMocks(invitation);

        var dto = new AcceptInvitationDto
        {
            Token = invitation.Token,
            FirstName = "New",
            LastName = "User",
            AcceptedPrivacyPolicy = true
        };

        var result = await _service.AcceptInvitationAsync(dto);

        Assert.IsTrue(result.Success);

        _userRepositoryMock.Verify(r => r.UpdateAsync(
            It.Is<User>(u => u.Claims.Contains(UserClaim.SystemAdmin) && u.Claims.Contains(UserClaim.Manager)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task AcceptInvitationAsync_InviterDeleted_SkipsClaims()
    {
        var deletedInviterId = Guid.NewGuid();
        // Inviter no longer exists in DB
        _userRepositoryMock.Setup(r => r.GetByIdAsync(deletedInviterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var invitation = CreateValidInvitation(deletedInviterId, "new@test.local",
            pendingClaims: new List<UserClaim> { UserClaim.Manager });

        SetupInvitationMocks(invitation);

        var dto = new AcceptInvitationDto
        {
            Token = invitation.Token,
            FirstName = "New",
            LastName = "User",
            AcceptedPrivacyPolicy = true
        };

        var result = await _service.AcceptInvitationAsync(dto);

        Assert.IsTrue(result.Success);

        // No claims should be applied since inviter is gone
        _userRepositoryMock.Verify(r => r.UpdateAsync(
            It.Is<User>(u => u.Claims.Count > 0),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task AcceptInvitationAsync_MultipleInvitations_MergesAuthorizedClaimsOnly()
    {
        var email = "new@test.local";

        var invitation1 = CreateValidInvitation(_managerUserId, email,
            pendingClaims: new List<UserClaim> { UserClaim.Manager, UserClaim.SystemAdmin });
        var invitation2 = CreateValidInvitation(_adminUserId, email,
            pendingClaims: new List<UserClaim> { UserClaim.SystemAdmin });

        // First invitation found by token
        _invitationTokenRepositoryMock
            .Setup(r => r.GetByTokenAsync(invitation1.Token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation1);

        // All valid invitations for email
        _invitationTokenRepositoryMock
            .Setup(r => r.GetAllValidByEmailAsync(email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<InvitationToken> { invitation1, invitation2 });

        _invitationTokenRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<InvitationToken>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InvitationToken t, CancellationToken _) => t);

        var dto = new AcceptInvitationDto
        {
            Token = invitation1.Token,
            FirstName = "New",
            LastName = "User",
            AcceptedPrivacyPolicy = true
        };

        var result = await _service.AcceptInvitationAsync(dto);

        Assert.IsTrue(result.Success);

        // Manager from invitation1 (manager can grant Manager) + SystemAdmin from invitation2 (admin can grant SystemAdmin)
        // SystemAdmin from invitation1 should be filtered out (manager cannot grant SystemAdmin)
        _userRepositoryMock.Verify(r => r.UpdateAsync(
            It.Is<User>(u => u.Claims.Contains(UserClaim.Manager) && u.Claims.Contains(UserClaim.SystemAdmin)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static InvitationToken CreateValidInvitation(
        Guid inviterId,
        string email,
        List<UserClaim>? pendingClaims = null)
    {
        return new InvitationToken
        {
            Id = Guid.NewGuid(),
            Email = email,
            Token = $"test-token-{Guid.NewGuid():N}",
            InvitedById = inviterId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            IsUsed = false,
            IsRevoked = false,
            PendingClaims = pendingClaims ?? new List<UserClaim>()
        };
    }

    [TestMethod]
    public async Task GetInvitationDetailsAsync_ReturnsPendingClaims()
    {
        var invitation = CreateValidInvitation(_adminUserId, "new@test.local",
            pendingClaims: new List<UserClaim> { UserClaim.Manager });
        SetupInvitationMocks(invitation);

        var details = await _service.GetInvitationDetailsAsync(invitation.Token);

        Assert.IsNotNull(details);
        CollectionAssert.AreEquivalent(new List<UserClaim> { UserClaim.Manager }, details.PendingClaims);
    }

    private void SetupInvitationMocks(InvitationToken invitation)
    {
        _invitationTokenRepositoryMock
            .Setup(r => r.GetByTokenAsync(invitation.Token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation);

        _invitationTokenRepositoryMock
            .Setup(r => r.GetAllValidByEmailAsync(invitation.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<InvitationToken> { invitation });

        _invitationTokenRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<InvitationToken>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InvitationToken t, CancellationToken _) => t);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _dbContext.Dispose();
    }
}
