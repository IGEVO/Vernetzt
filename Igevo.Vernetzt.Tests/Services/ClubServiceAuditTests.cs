using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Exceptions;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Data;
using Igevo.Vernetzt.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Igevo.Vernetzt.Tests.TestInfrastructure;

namespace Igevo.Vernetzt.Tests.Services;

[TestClass]
public class ClubServiceAuditTests
{
    private Mock<IClubRepository> _clubRepositoryMock = null!;
    private Mock<IUserRepository> _userRepositoryMock = null!;
    private Mock<IAuthorizationService> _authorizationServiceMock = null!;
    private Mock<IAuditLogRepository> _auditLogRepositoryMock = null!;
    private Mock<IEmailService> _emailServiceMock = null!;
    private Mock<IAvatarService> _avatarServiceMock = null!;
    private Mock<ILogger<ClubService>> _loggerMock = null!;
    private VernetztDbContext _dbContext = null!;
    private ClubService _service = null!;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _clubId = Guid.NewGuid();
    private string? _capturedOldValues;
    private string? _capturedNewValues;
    private Club _existingClub = null!;

    [TestInitialize]
    public void Setup()
    {
        _clubRepositoryMock = new Mock<IClubRepository>();
        _userRepositoryMock = new Mock<IUserRepository>();
        _authorizationServiceMock = new Mock<IAuthorizationService>();
        _auditLogRepositoryMock = new Mock<IAuditLogRepository>();
        _emailServiceMock = new Mock<IEmailService>();
        _avatarServiceMock = new Mock<IAvatarService>();
        _loggerMock = new Mock<ILogger<ClubService>>();

        _dbContext = SqliteTestDatabase.CreateContext();

        _existingClub = new Club
        {
            Id = _clubId,
            Name = "TSV Musterstadt 1900",
            ClubTypeId = Guid.NewGuid(),
            Street = "Musterweg 1",
            PostalCode = "12345",
            City = "Musterstadt",
            WebsiteUrl = "https://verein.example",
            Email = "info@verein.example"
        };

        _authorizationServiceMock
            .Setup(s => s.CanEditClubAsync(_userId, _clubId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _clubRepositoryMock
            .Setup(r => r.GetByIdAsync(_clubId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_existingClub);

        _clubRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<Club>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Club entity, CancellationToken _) => entity);

        _auditLogRepositoryMock
            .Setup(r => r.LogAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<AuditAction>(),
                It.IsAny<Guid>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Guid, AuditAction, Guid, string?, string?, CancellationToken>((_, _, _, _, oldValues, newValues, _) =>
            {
                _capturedOldValues = oldValues;
                _capturedNewValues = newValues;
            })
            .Returns(Task.CompletedTask);

        _service = new ClubService(
            _clubRepositoryMock.Object,
            _userRepositoryMock.Object,
            _authorizationServiceMock.Object,
            _auditLogRepositoryMock.Object,
            _emailServiceMock.Object,
            _avatarServiceMock.Object,
            _dbContext,
            _loggerMock.Object);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _dbContext.Dispose();
    }

    [TestMethod]
    public async Task UpdateAsync_AddressChanged_LogsOnlyAddressDiffInAudit()
    {
        var updateRequest = new Club
        {
            Name = "TSV Musterstadt 1900",
            ClubTypeId = _existingClub.ClubTypeId,
            Street = "Neue Strasse 7",
            PostalCode = _existingClub.PostalCode,
            City = _existingClub.City,
            WebsiteUrl = _existingClub.WebsiteUrl,
            Email = _existingClub.Email
        };

        var result = await _service.UpdateAsync(_userId, _clubId, updateRequest);

        Assert.IsNotNull(result);
        Assert.IsFalse(string.IsNullOrWhiteSpace(_capturedOldValues));
        Assert.IsFalse(string.IsNullOrWhiteSpace(_capturedNewValues));

        using var oldDoc = JsonDocument.Parse(_capturedOldValues!);
        using var newDoc = JsonDocument.Parse(_capturedNewValues!);

        Assert.IsTrue(oldDoc.RootElement.TryGetProperty("Street", out var oldStreet));
        Assert.AreEqual("Musterweg 1", oldStreet.GetString());
        Assert.IsTrue(newDoc.RootElement.TryGetProperty("Street", out var newStreet));
        Assert.AreEqual("Neue Strasse 7", newStreet.GetString());

        Assert.IsFalse(oldDoc.RootElement.TryGetProperty("Name", out _));
        Assert.IsFalse(newDoc.RootElement.TryGetProperty("Name", out _));
    }

    [TestMethod]
    public async Task UpdateAsync_SocialMediaLinksChanged_NormalizesAndLogsDiff()
    {
        var updateRequest = new Club
        {
            Name = _existingClub.Name,
            ClubTypeId = _existingClub.ClubTypeId,
            Street = _existingClub.Street,
            PostalCode = _existingClub.PostalCode,
            City = _existingClub.City,
            WebsiteUrl = _existingClub.WebsiteUrl,
            Email = _existingClub.Email,
            SocialMediaLinks =
            [
                new SocialMediaLink(SocialMediaPlatform.Instagram, " https://instagram.com/tsv "),
                new SocialMediaLink(SocialMediaPlatform.Instagram, "https://INSTAGRAM.com/tsv"),
                new SocialMediaLink(SocialMediaPlatform.Facebook, "   ")
            ]
        };

        var result = await _service.UpdateAsync(_userId, _clubId, updateRequest);

        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.SocialMediaLinks.Count);
        Assert.AreEqual(new SocialMediaLink(SocialMediaPlatform.Instagram, "https://instagram.com/tsv"), result.SocialMediaLinks[0]);

        using var newDoc = JsonDocument.Parse(_capturedNewValues!);
        Assert.AreEqual("Instagram: https://instagram.com/tsv", newDoc.RootElement.GetProperty("SocialMediaLinks").GetString());
        using var oldDoc = JsonDocument.Parse(_capturedOldValues!);
        Assert.AreEqual(JsonValueKind.Null, oldDoc.RootElement.GetProperty("SocialMediaLinks").ValueKind);
    }

    [TestMethod]
    public async Task UpdateAsync_WhitespaceOnlyDifferences_LogsNoFieldDiff()
    {
        var updateRequest = new Club
        {
            Name = "  TSV Musterstadt 1900  ",
            ClubTypeId = _existingClub.ClubTypeId,
            Street = "  Musterweg 1  ",
            PostalCode = " 12345 ",
            City = " Musterstadt ",
            WebsiteUrl = " https://verein.example ",
            Email = " info@verein.example "
        };

        var result = await _service.UpdateAsync(_userId, _clubId, updateRequest);

        Assert.IsNotNull(result);
        Assert.IsNull(_capturedOldValues);
        Assert.IsNull(_capturedNewValues);
    }

    [TestMethod]
    public async Task DeleteAsync_WhenDeletionSucceeds_DeletesClubAndDepartmentAvatars()
    {
        var managerUser = new User
        {
            Id = _userId,
            Email = "manager@test.local",
            FirstName = "Manager",
            LastName = "User",
            Claims = [UserClaim.Manager]
        };

        var department1Id = Guid.NewGuid();
        var department2Id = Guid.NewGuid();

        _existingClub.Departments = new List<Department>
        {
            new() { Id = department1Id, ClubId = _clubId, Name = "Jugend" },
            new() { Id = department2Id, ClubId = _clubId, Name = "Senioren" }
        };

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(managerUser);

        _clubRepositoryMock
            .Setup(r => r.DeleteAsync(_clubId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _avatarServiceMock
            .Setup(s => s.DeleteDepartmentAvatarAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _avatarServiceMock
            .Setup(s => s.DeleteClubAvatarAsync(_clubId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _service.DeleteAsync(_userId, _clubId);

        Assert.IsTrue(result);
        _avatarServiceMock.Verify(s => s.DeleteDepartmentAvatarAsync(department1Id, It.IsAny<CancellationToken>()), Times.Once);
        _avatarServiceMock.Verify(s => s.DeleteDepartmentAvatarAsync(department2Id, It.IsAny<CancellationToken>()), Times.Once);
        _avatarServiceMock.Verify(s => s.DeleteClubAvatarAsync(_clubId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task DeleteAsync_WhenDeletionFails_DoesNotDeleteAvatars()
    {
        var managerUser = new User
        {
            Id = _userId,
            Email = "manager@test.local",
            FirstName = "Manager",
            LastName = "User",
            Claims = [UserClaim.Manager]
        };

        _existingClub.Departments = new List<Department>
        {
            new() { Id = Guid.NewGuid(), ClubId = _clubId, Name = "Jugend" }
        };

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(managerUser);

        _clubRepositoryMock
            .Setup(r => r.DeleteAsync(_clubId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _service.DeleteAsync(_userId, _clubId);

        Assert.IsFalse(result);
        _avatarServiceMock.Verify(s => s.DeleteDepartmentAvatarAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _avatarServiceMock.Verify(s => s.DeleteClubAvatarAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task AddContactPersonAsync_NotificationFails_StillAssignsContactPerson()
    {
        var contactPerson = new User { Id = Guid.NewGuid(), Email = "kontakt@test.local", FirstName = "Kim", LastName = "Kontakt" };
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(contactPerson.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contactPerson);
        _emailServiceMock
            .Setup(s => s.SendClubAssignmentConsentRequestAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMTP down"));

        var result = await _service.AddContactPersonAsync(_userId, _clubId, contactPerson.Id);

        Assert.IsTrue(result);
        Assert.IsTrue(_existingClub.ContactAssignments.Any(ca => ca.UserId == contactPerson.Id));
        _clubRepositoryMock.Verify(r => r.UpdateAsync(_existingClub, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task AddContactPersonAsync_CreatesPendingInternalAssignment()
    {
        var contactPerson = new User { Id = Guid.NewGuid(), Email = "new@verein.example", FirstName = "Neu", LastName = "Person" };
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(contactPerson.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contactPerson);

        var result = await _service.AddContactPersonAsync(_userId, _clubId, contactPerson.Id, canEdit: true);

        Assert.IsTrue(result);
        var assignment = _existingClub.ContactAssignments.Single(ca => ca.UserId == contactPerson.Id);
        Assert.IsNull(assignment.ConfirmedAt, "New assignments wait for the user's consent.");
        Assert.AreEqual(VisibilityLevel.Internal, assignment.Visibility);
        Assert.AreEqual(_userId, assignment.AssignedById);
        _emailServiceMock.Verify(s => s.SendClubAssignmentConsentRequestAsync(
            contactPerson.Email, It.IsAny<string>(), It.IsAny<string>(), assignment.Id, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task AddContactPersonAsync_PendingAssignmentExists_ThrowsConflict()
    {
        var contactPerson = new User { Id = Guid.NewGuid(), Email = "pending@verein.example", FirstName = "Pen", LastName = "Ding" };
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(contactPerson.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contactPerson);
        _existingClub.ContactAssignments.Add(new ContactAssignment { UserId = contactPerson.Id, ClubId = _clubId });

        var ex = await Assert.ThrowsExactlyAsync<ContactAssignmentConflictException>(
            () => _service.AddContactPersonAsync(_userId, _clubId, contactPerson.Id));

        StringAssert.Contains(ex.Message, "Zustimmung");
    }
}
