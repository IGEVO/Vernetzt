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
using System.Text.Json;

using Igevo.Vernetzt.Tests.TestInfrastructure;
namespace Igevo.Vernetzt.Tests.Services;

/// <summary>
/// Unit tests for GDPR features in UserService (export + delete).
/// </summary>
[TestClass]
public class GdprServiceTests
{
    private Mock<IUserRepository> _userRepositoryMock = null!;
    private Mock<IClubRepository> _clubRepositoryMock = null!;
    private Mock<IDepartmentRepository> _departmentRepositoryMock = null!;
    private Mock<IContactPositionRepository> _contactPositionRepositoryMock = null!;
    private Mock<IAuthorizationService> _authorizationServiceMock = null!;
    private Mock<IAuditLogRepository> _auditLogRepositoryMock = null!;
    private Mock<IEmailService> _emailServiceMock = null!;
    private Mock<IAvatarService> _avatarServiceMock = null!;
    private Mock<IInvitationTokenRepository> _invitationTokenRepositoryMock = null!;
    private Mock<ILogger<UserService>> _loggerMock = null!;
    private VernetztDbContext _dbContext = null!;
    private UserService _service = null!;

    private readonly Guid _adminUserId = Guid.NewGuid();
    private readonly Guid _regularUserId = Guid.NewGuid();
    private readonly Guid _clubId = Guid.NewGuid();

    [TestInitialize]
    public void Setup()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _clubRepositoryMock = new Mock<IClubRepository>();
        _departmentRepositoryMock = new Mock<IDepartmentRepository>();
        _contactPositionRepositoryMock = new Mock<IContactPositionRepository>();
        _authorizationServiceMock = new Mock<IAuthorizationService>();
        _auditLogRepositoryMock = new Mock<IAuditLogRepository>();
        _emailServiceMock = new Mock<IEmailService>();
        _avatarServiceMock = new Mock<IAvatarService>();
        _invitationTokenRepositoryMock = new Mock<IInvitationTokenRepository>();
        _loggerMock = new Mock<ILogger<UserService>>();
        _dbContext = SqliteTestDatabase.CreateContext();

        var appSettings = Options.Create(new AppSettings
        {
            InvitationTokenExpirationDays = 7
        });

        var appSettingsServiceMock = new Mock<IAppSettingsService>();

        _service = new UserService(
            _userRepositoryMock.Object,
            _clubRepositoryMock.Object,
            _departmentRepositoryMock.Object,
            _contactPositionRepositoryMock.Object,
            _authorizationServiceMock.Object,
            _auditLogRepositoryMock.Object,
            _emailServiceMock.Object,
            _invitationTokenRepositoryMock.Object,
            appSettingsServiceMock.Object,
            _dbContext,
            appSettings,
            _loggerMock.Object,
            _avatarServiceMock.Object);

        SetupTestUsers();
        SetupTestData();
    }

    private void SetupTestUsers()
    {
        var adminUser = new User
        {
            Id = _adminUserId,
            Email = "admin@test.com",
            FirstName = "Admin",
            LastName = "User",
            PhoneNumber = "123456",
            EmailVisibility = VisibilityLevel.AllUsers,
            PhoneVisibility = VisibilityLevel.AllUsers,
            Claims = new List<UserClaim> { UserClaim.SystemAdmin },
            CreatedAt = DateTime.UtcNow.AddDays(-30)
        };

        var regularUser = new User
        {
            Id = _regularUserId,
            Email = "regular@test.com",
            FirstName = "Regular",
            LastName = "User",
            PhoneNumber = "654321",
            EmailVisibility = VisibilityLevel.AllUsers,
            PhoneVisibility = VisibilityLevel.Internal,
            Claims = new List<UserClaim>(),
            CreatedAt = DateTime.UtcNow.AddDays(-10)
        };

        _userRepositoryMock.Setup(r => r.GetByIdAsync(_adminUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(adminUser);
        _userRepositoryMock.Setup(r => r.GetByIdAsync(_regularUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(regularUser);
        _userRepositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<User> { adminUser, regularUser });
    }

    private void SetupTestData()
    {
        var club = new Club
        {
            Id = _clubId,
            Name = "Test Club",
            ContactAssignments = new List<ContactAssignment>
            {
                new()
                {
                    UserId = _regularUserId,
                    ClubId = _clubId,
                    AssignedAt = DateTime.UtcNow,
                    ContextEmail = "kontakt@test-club.de",
                    ContextEmailVerified = true,
                    ContextEmailVerifiedAt = DateTime.UtcNow.AddDays(-1),
                    ContextEmailVisibility = VisibilityLevel.Internal
                }
            }
        };

        _clubRepositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Club> { club });
        _clubRepositoryMock.Setup(r => r.GetAllWithDetailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Club> { club });
        _clubRepositoryMock.Setup(r => r.GetByIdAsync(_clubId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(club);

        _departmentRepositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Department>());
        _departmentRepositoryMock.Setup(r => r.GetAllWithDetailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Department>());

        _auditLogRepositoryMock.Setup(r => r.GetForDataSubjectAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuditLog>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    Action = AuditAction.Update,
                    EntityType = "User",
                    EntityId = _regularUserId,
                    UserId = _regularUserId,
                    Timestamp = DateTime.UtcNow.AddDays(-5)
                }
            });
    }

    #region ExportUserDataAsync Tests

    [TestMethod]
    public async Task ExportUserDataAsync_OwnData_ReturnsExport()
    {
        var result = await _service.ExportUserDataAsync(_regularUserId, _regularUserId);

        Assert.IsNotNull(result);
        Assert.AreEqual("Regular", result.Personal.FirstName);
        Assert.AreEqual("User", result.Personal.LastName);
        Assert.AreEqual("regular@test.com", result.Personal.Email);
        Assert.AreEqual("654321", result.Personal.PhoneNumber);
    }

    [TestMethod]
    public async Task ExportUserDataAsync_UserWithAvatar_ReferencesProfilePictureInMedia()
    {
        (await _userRepositoryMock.Object.GetByIdAsync(_regularUserId))!.HasAvatar = true;
        _avatarServiceMock.Setup(a => a.GetUserAvatarAsync(_regularUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 1, 2, 3 });

        var result = await _service.ExportUserDataAsync(_regularUserId, _regularUserId);

        var media = result.Media.Single();
        Assert.AreEqual("ProfilePicture", media.Type);
        Assert.AreEqual("media/profilbild.webp", media.Path);
        Assert.AreEqual("image/webp", media.ContentType);
        Assert.AreEqual(3, media.SizeBytes);
    }

    [TestMethod]
    public async Task ExportUserDataAsync_UserWithoutAvatar_HasNoMedia()
    {
        var result = await _service.ExportUserDataAsync(_regularUserId, _regularUserId);

        Assert.AreEqual(0, result.Media.Count);
    }

    [TestMethod]
    public async Task ExportUserDataArchiveAsync_UserWithAvatar_ContainsJsonAndProfilePicture()
    {
        (await _userRepositoryMock.Object.GetByIdAsync(_regularUserId))!.HasAvatar = true;
        var avatarBytes = new byte[] { 1, 2, 3 };
        _avatarServiceMock.Setup(a => a.GetUserAvatarAsync(_regularUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(avatarBytes);

        var archiveBytes = await _service.ExportUserDataArchiveAsync(_regularUserId, _regularUserId);

        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(archiveBytes), System.IO.Compression.ZipArchiveMode.Read);
        Assert.IsNotNull(zip.GetEntry("datenexport.json"));
        var avatarEntry = zip.GetEntry("media/profilbild.webp");
        Assert.IsNotNull(avatarEntry);
        using var content = new MemoryStream();
        await avatarEntry.Open().CopyToAsync(content);
        CollectionAssert.AreEqual(avatarBytes, content.ToArray());
    }

    [TestMethod]
    public async Task ExportUserDataAsync_Admin_CanExportOtherUser()
    {
        var result = await _service.ExportUserDataAsync(_adminUserId, _regularUserId);

        Assert.IsNotNull(result);
        Assert.AreEqual("regular@test.com", result.Personal.Email);
        Assert.AreEqual("admin@test.com", result.Metadata.RequestedBy);
    }

    [TestMethod]
    public async Task ExportUserDataAsync_RegularUser_CannotExportOther()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.ExportUserDataAsync(_regularUserId, _adminUserId));
    }

    [TestMethod]
    public async Task ExportUserDataAsync_IncludesAssignments()
    {
        var result = await _service.ExportUserDataAsync(_regularUserId, _regularUserId);

        Assert.IsNotNull(result.Assignments);
        Assert.AreEqual(1, result.Assignments.Count);
        Assert.AreEqual("Club", result.Assignments[0].Type);
        Assert.AreEqual("Test Club", result.Assignments[0].EntityName);
    }

    [TestMethod]
    public async Task ExportUserDataAsync_IncludesContextEmailDataInAssignments()
    {
        var result = await _service.ExportUserDataAsync(_regularUserId, _regularUserId);

        Assert.AreEqual("kontakt@test-club.de", result.Assignments[0].ContextEmail);
        Assert.IsTrue(result.Assignments[0].ContextEmailVerified);
        Assert.IsNotNull(result.Assignments[0].ContextEmailVerifiedAt);
        Assert.AreEqual("Internal", result.Assignments[0].ContextEmailVisibility);
    }

    [TestMethod]
    public async Task ExportUserDataAsync_IncludesAuditLog()
    {
        var result = await _service.ExportUserDataAsync(_regularUserId, _regularUserId);

        Assert.IsNotNull(result.AuditLog);
        Assert.AreEqual(1, result.AuditLog.Count);
        Assert.AreEqual("Update", result.AuditLog[0].Action);
    }

    [TestMethod]
    public async Task ExportUserDataAsync_MailAuditEntry_ContainsOnlySubjectAndRecipientCount()
    {
        _auditLogRepositoryMock.Setup(r => r.GetForDataSubjectAsync(_regularUserId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuditLog>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    Action = AuditAction.EmailRelayForward,
                    EntityType = "EmailRelayToken",
                    EntityId = Guid.NewGuid(),
                    UserId = _regularUserId,
                    Timestamp = DateTime.UtcNow.AddMinutes(-5),
                    NewValues = JsonSerializer.Serialize(new
                    {
                        Subject = "Hinweis",
                        RecipientCount = 2,
                        Recipients = new[]
                        {
                            new { Name = "Kontakt Eins", EmailMasked = "k***@verein.de" },
                            new { Name = "Kontakt Zwei", EmailMasked = "z***@verein.de" }
                        }
                    })
                }
            });

        var result = await _service.ExportUserDataAsync(_regularUserId, _regularUserId);
        var exportedJson = result.AuditLog[0].NewValues;

        Assert.IsNotNull(exportedJson);
        Assert.IsFalse(exportedJson!.Contains("EmailMasked", StringComparison.Ordinal));
        Assert.IsFalse(exportedJson.Contains("Kontakt Eins", StringComparison.Ordinal));

        using var doc = JsonDocument.Parse(exportedJson);
        Assert.AreEqual("Hinweis", doc.RootElement.GetProperty("Subject").GetString());
        Assert.AreEqual(2, doc.RootElement.GetProperty("RecipientCount").GetInt32());
        Assert.IsFalse(doc.RootElement.TryGetProperty("Recipients", out _));
    }

    [TestMethod]
    public async Task ExportUserDataAsync_IncludesAccountData()
    {
        var result = await _service.ExportUserDataAsync(_regularUserId, _regularUserId);

        Assert.AreEqual(_regularUserId, result.Account.UserId);
    }

    [TestMethod]
    public async Task ExportUserDataAsync_IncludesPrivacySettings()
    {
        var result = await _service.ExportUserDataAsync(_regularUserId, _regularUserId);

        Assert.AreEqual("AllUsers", result.Privacy.EmailVisibility);
        Assert.AreEqual("Internal", result.Privacy.PhoneVisibility);
    }

    [TestMethod]
    public async Task ExportUserDataAsync_IncludesMetadata()
    {
        var result = await _service.ExportUserDataAsync(_regularUserId, _regularUserId);

        Assert.AreEqual("1.0", result.Metadata.ExportVersion);
        Assert.AreEqual("regular@test.com", result.Metadata.RequestedBy);
        Assert.IsTrue(result.Metadata.ExportedAt <= DateTime.UtcNow);
    }

    [TestMethod]
    public async Task ExportUserDataAsync_DoesNotLogReadAccessAuditEntry()
    {
        await _service.ExportUserDataAsync(_regularUserId, _regularUserId);

        _auditLogRepositoryMock.Verify(r => r.LogAsync(
            It.IsAny<AuditLog>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ExportUserDataAsync_NonExistentUser_ThrowsInvalidOperation()
    {
        var nonExistentUserId = Guid.NewGuid();
        _userRepositoryMock.Setup(r => r.GetByIdAsync(nonExistentUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.ExportUserDataAsync(nonExistentUserId, nonExistentUserId));
    }

    #endregion

    #region DeleteAccountAsync Tests

    [TestMethod]
    public async Task DeleteAccountAsync_OwnAccount_Success()
    {
        var regularUser = (await _userRepositoryMock.Object.GetByIdAsync(_regularUserId))!;
        _userRepositoryMock.Setup(r => r.GetByIdWithAssignmentsAsync(_regularUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(regularUser);
        _userRepositoryMock.Setup(r => r.DeleteAsync(_regularUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _service.DeleteAccountAsync(_regularUserId, _regularUserId, "regular@test.com", "Test deletion");

        Assert.IsTrue(result.Success);
        Assert.AreEqual("Konto erfolgreich gelöscht.", result.Message);
        Assert.IsNotNull(result.DeletedAt);
        _userRepositoryMock.Verify(r => r.DeleteAsync(_regularUserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task DeleteAccountAsync_CleansUpInvitationTokens()
    {
        var regularUser = (await _userRepositoryMock.Object.GetByIdAsync(_regularUserId))!;
        _userRepositoryMock.Setup(r => r.GetByIdWithAssignmentsAsync(_regularUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(regularUser);
        _userRepositoryMock.Setup(r => r.DeleteAsync(_regularUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _invitationTokenRepositoryMock.Setup(r => r.DeleteByRecipientAddressAsync("regular@test.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        var result = await _service.DeleteAccountAsync(_regularUserId, _regularUserId, "regular@test.com");

        Assert.IsTrue(result.Success);
        _invitationTokenRepositoryMock.Verify(r => r.DeleteByRecipientAddressAsync("regular@test.com", It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task DeleteAccountAsync_DeletesUser_FromDatabase()
    {
        var regularUser = (await _userRepositoryMock.Object.GetByIdAsync(_regularUserId))!;
        _userRepositoryMock.Setup(r => r.GetByIdWithAssignmentsAsync(_regularUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(regularUser);
        _userRepositoryMock.Setup(r => r.DeleteAsync(_regularUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _service.DeleteAccountAsync(_regularUserId, _regularUserId, "regular@test.com", "Test deletion");

        Assert.IsTrue(result.Success);
        _userRepositoryMock.Verify(r => r.DeleteAsync(_regularUserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task DeleteAccountAsync_WrongEmail_Fails()
    {
        var result = await _service.DeleteAccountAsync(_regularUserId, _regularUserId, "wrong@email.com");

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Message.Contains("E-Mail-Bestätigung"));
        _userRepositoryMock.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task DeleteAccountAsync_CaseInsensitiveEmailMatch()
    {
        var regularUser = (await _userRepositoryMock.Object.GetByIdAsync(_regularUserId))!;
        _userRepositoryMock.Setup(r => r.GetByIdWithAssignmentsAsync(_regularUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(regularUser);
        _userRepositoryMock.Setup(r => r.DeleteAsync(_regularUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _service.DeleteAccountAsync(_regularUserId, _regularUserId, "REGULAR@TEST.COM");

        Assert.IsTrue(result.Success);
    }

    [TestMethod]
    public async Task DeleteAccountAsync_LastAdmin_Prevented()
    {
        var result = await _service.DeleteAccountAsync(_adminUserId, _adminUserId, "admin@test.com");

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Message.Contains("System-Administrator"));
        _userRepositoryMock.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task DeleteAccountAsync_ConfiguredSystemAdmin_Prevented()
    {
        // Set up a scenario where SystemAdminEmail matches the user
        var appSettings = Options.Create(new AppSettings
        {
            SystemAdminEmail = "admin@test.com",
            InvitationTokenExpirationDays = 7
        });

        var service = new UserService(
            _userRepositoryMock.Object,
            _clubRepositoryMock.Object,
            _departmentRepositoryMock.Object,
            _contactPositionRepositoryMock.Object,
            _authorizationServiceMock.Object,
            _auditLogRepositoryMock.Object,
            _emailServiceMock.Object,
            _invitationTokenRepositoryMock.Object,
            new Mock<IAppSettingsService>().Object,
            _dbContext,
            appSettings,
            _loggerMock.Object,
            _avatarServiceMock.Object);

        var result = await service.DeleteAccountAsync(_adminUserId, _adminUserId, "admin@test.com");

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Message.Contains("System-Administrator"));
    }

    [TestMethod]
    public async Task DeleteAccountAsync_AdminCanDeleteOther()
    {
        var regularUser = (await _userRepositoryMock.Object.GetByIdAsync(_regularUserId))!;
        _userRepositoryMock.Setup(r => r.GetByIdWithAssignmentsAsync(_regularUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(regularUser);
        _userRepositoryMock.Setup(r => r.DeleteAsync(_regularUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _service.DeleteAccountAsync(_adminUserId, _regularUserId, "regular@test.com", "Admin deleting user");

        Assert.IsTrue(result.Success);
    }

    [TestMethod]
    public async Task DeleteAccountAsync_Success_DeletesUserAvatar()
    {
        var regularUser = (await _userRepositoryMock.Object.GetByIdAsync(_regularUserId))!;
        _userRepositoryMock.Setup(r => r.GetByIdWithAssignmentsAsync(_regularUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(regularUser);
        _userRepositoryMock.Setup(r => r.DeleteAsync(_regularUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _avatarServiceMock.Setup(s => s.DeleteUserAvatarAsync(_regularUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _service.DeleteAccountAsync(_regularUserId, _regularUserId, "regular@test.com");

        Assert.IsTrue(result.Success);
        _avatarServiceMock.Verify(s => s.DeleteUserAvatarAsync(_regularUserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task DeleteAccountAsync_AvatarCleanupFails_StillDeletesAccountAndReportsIt()
    {
        var regularUser = (await _userRepositoryMock.Object.GetByIdAsync(_regularUserId))!;
        _userRepositoryMock.Setup(r => r.GetByIdWithAssignmentsAsync(_regularUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(regularUser);
        _userRepositoryMock.Setup(r => r.DeleteAsync(_regularUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _avatarServiceMock.Setup(s => s.DeleteUserAvatarAsync(_regularUserId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("disk error"));

        var result = await _service.DeleteAccountAsync(_regularUserId, _regularUserId, "regular@test.com");

        Assert.IsTrue(result.Success);
        Assert.IsTrue(result.MediaCleanupFailed);
        _userRepositoryMock.Verify(r => r.DeleteAsync(_regularUserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task DeleteAccountAsync_RegularUserCannotDeleteOther()
    {
        var result = await _service.DeleteAccountAsync(_regularUserId, _adminUserId, "admin@test.com");

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Message.Contains("eigenes Konto"));
    }

    [TestMethod]
    public async Task DeleteAccountAsync_LogsAuditEntry()
    {
        var regularUser = (await _userRepositoryMock.Object.GetByIdAsync(_regularUserId))!;
        _userRepositoryMock.Setup(r => r.GetByIdWithAssignmentsAsync(_regularUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(regularUser);
        _userRepositoryMock.Setup(r => r.DeleteAsync(_regularUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _service.DeleteAccountAsync(_regularUserId, _regularUserId, "regular@test.com", "GDPR request");

        _auditLogRepositoryMock.Verify(r => r.LogAsync(
            It.Is<AuditLog>(a =>
                a.Action == AuditAction.Delete &&
                a.EntityType == "User" &&
                a.EntityId == _regularUserId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task DeleteAccountAsync_ReturnsAffectedAssignmentsCount()
    {
        var regularUser = (await _userRepositoryMock.Object.GetByIdAsync(_regularUserId))!;
        regularUser.ContactAssignments = new List<ContactAssignment>
        {
            new() { UserId = _regularUserId, ClubId = _clubId },
            new() { UserId = _regularUserId, DepartmentId = Guid.NewGuid() }
        };
        _userRepositoryMock.Setup(r => r.GetByIdWithAssignmentsAsync(_regularUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(regularUser);
        _userRepositoryMock.Setup(r => r.DeleteAsync(_regularUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _service.DeleteAccountAsync(_regularUserId, _regularUserId, "regular@test.com");

        Assert.IsTrue(result.Success);
        Assert.AreEqual(2, result.AffectedAssignments);
    }

    [TestMethod]
    public async Task DeleteAccountAsync_NonExistentUser_ReturnsFailure()
    {
        var nonExistentId = Guid.NewGuid();
        _userRepositoryMock.Setup(r => r.GetByIdAsync(nonExistentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var result = await _service.DeleteAccountAsync(nonExistentId, nonExistentId, "test@test.com");

        Assert.IsFalse(result.Success);
    }

    [TestMethod]
    public async Task DeleteAccountAsync_EmptyEmail_Fails()
    {
        var result = await _service.DeleteAccountAsync(_regularUserId, _regularUserId, "");

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Message.Contains("E-Mail-Bestätigung"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        _dbContext.Dispose();
    }

    #endregion
}

