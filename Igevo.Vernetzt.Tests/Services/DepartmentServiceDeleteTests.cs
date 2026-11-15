using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Data;
using Igevo.Vernetzt.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Igevo.Vernetzt.Tests.TestInfrastructure;

namespace Igevo.Vernetzt.Tests.Services;

[TestClass]
public class DepartmentServiceDeleteTests
{
    private Mock<IDepartmentRepository> _departmentRepositoryMock = null!;
    private Mock<IClubRepository> _clubRepositoryMock = null!;
    private Mock<IUserRepository> _userRepositoryMock = null!;
    private Mock<IAuthorizationService> _authorizationServiceMock = null!;
    private Mock<IAuditLogRepository> _auditLogRepositoryMock = null!;
    private Mock<IEmailService> _emailServiceMock = null!;
    private Mock<IAvatarService> _avatarServiceMock = null!;
    private Mock<ILogger<DepartmentService>> _loggerMock = null!;
    private VernetztDbContext _dbContext = null!;
    private DepartmentService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _departmentRepositoryMock = new Mock<IDepartmentRepository>();
        _clubRepositoryMock = new Mock<IClubRepository>();
        _userRepositoryMock = new Mock<IUserRepository>();
        _authorizationServiceMock = new Mock<IAuthorizationService>();
        _auditLogRepositoryMock = new Mock<IAuditLogRepository>();
        _emailServiceMock = new Mock<IEmailService>();
        _avatarServiceMock = new Mock<IAvatarService>();
        _loggerMock = new Mock<ILogger<DepartmentService>>();

        _dbContext = SqliteTestDatabase.CreateContext();

        _service = new DepartmentService(
            _departmentRepositoryMock.Object,
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
    public async Task DeleteAsync_WhenDeletionSucceeds_DeletesDepartmentAvatar()
    {
        var userId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var department = new Department
        {
            Id = departmentId,
            ClubId = Guid.NewGuid(),
            Name = "Jugend"
        };

        _authorizationServiceMock
            .Setup(s => s.CanEditClubAsync(userId, department.ClubId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _departmentRepositoryMock
            .Setup(r => r.GetByIdAsync(departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(department);

        _departmentRepositoryMock
            .Setup(r => r.DeleteAsync(departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _avatarServiceMock
            .Setup(s => s.DeleteDepartmentAvatarAsync(departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _service.DeleteAsync(userId, departmentId);

        Assert.IsTrue(result);
        _avatarServiceMock.Verify(s => s.DeleteDepartmentAvatarAsync(departmentId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task DeleteAsync_WhenDeletionFails_DoesNotDeleteDepartmentAvatar()
    {
        var userId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var department = new Department
        {
            Id = departmentId,
            ClubId = Guid.NewGuid(),
            Name = "Jugend"
        };

        _authorizationServiceMock
            .Setup(s => s.CanEditClubAsync(userId, department.ClubId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _departmentRepositoryMock
            .Setup(r => r.GetByIdAsync(departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(department);

        _departmentRepositoryMock
            .Setup(r => r.DeleteAsync(departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _service.DeleteAsync(userId, departmentId);

        Assert.IsFalse(result);
        _avatarServiceMock.Verify(s => s.DeleteDepartmentAvatarAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task DeleteAsync_WhenNotAuthorized_DoesNotDeleteDepartmentAvatar()
    {
        var userId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var department = new Department
        {
            Id = departmentId,
            ClubId = Guid.NewGuid(),
            Name = "Jugend"
        };

        _departmentRepositoryMock
            .Setup(r => r.GetByIdAsync(departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(department);

        _authorizationServiceMock
            .Setup(s => s.CanEditClubAsync(userId, department.ClubId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _service.DeleteAsync(userId, departmentId);

        Assert.IsFalse(result);
        _departmentRepositoryMock.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _avatarServiceMock.Verify(s => s.DeleteDepartmentAvatarAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task DeleteAsync_ByDepartmentEditorWithoutClubRights_IsRejected()
    {
        var userId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var department = new Department
        {
            Id = departmentId,
            ClubId = Guid.NewGuid(),
            Name = "Jugend"
        };

        _departmentRepositoryMock
            .Setup(r => r.GetByIdAsync(departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(department);

        // Department-level edit rights alone must not allow deleting the department
        _authorizationServiceMock
            .Setup(s => s.CanEditDepartmentAsync(userId, departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _authorizationServiceMock
            .Setup(s => s.CanEditClubAsync(userId, department.ClubId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _service.DeleteAsync(userId, departmentId);

        Assert.IsFalse(result);
        _departmentRepositoryMock.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task AddContactPersonAsync_NotificationFails_StillAssignsContactPerson()
    {
        var userId = Guid.NewGuid();
        var department = new Department { Id = Guid.NewGuid(), ClubId = Guid.NewGuid(), Name = "Jugend" };
        var contactPerson = new User { Id = Guid.NewGuid(), Email = "kontakt@test.local", FirstName = "Kim", LastName = "Kontakt" };

        _authorizationServiceMock
            .Setup(s => s.CanEditDepartmentAsync(userId, department.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _departmentRepositoryMock
            .Setup(r => r.GetByIdAsync(department.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(department);
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(contactPerson.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contactPerson);
        _emailServiceMock
            .Setup(s => s.SendDepartmentAssignmentConsentRequestAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMTP down"));

        var result = await _service.AddContactPersonAsync(userId, department.Id, contactPerson.Id);

        Assert.IsTrue(result);
        Assert.IsTrue(department.ContactAssignments.Any(ca => ca.UserId == contactPerson.Id));
        _departmentRepositoryMock.Verify(r => r.UpdateAsync(department, It.IsAny<CancellationToken>()), Times.Once);
    }
}
