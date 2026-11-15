using Igevo.Vernetzt.Core.DTOs.Users;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Data;
using Igevo.Vernetzt.Infrastructure.Services;
using Igevo.Vernetzt.Tests.TestInfrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace Igevo.Vernetzt.Tests.Services;

[TestClass]
public class ContactAssignmentConsentServiceTests
{
    private VernetztDbContext _dbContext = null!;
    private Mock<IAuthorizationService> _authorizationServiceMock = null!;
    private Mock<IEmailService> _emailServiceMock = null!;
    private ContactAssignmentConsentService _service = null!;

    private User _assignedUser = null!;
    private User _assigner = null!;
    private Club _club = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbContext = SqliteTestDatabase.CreateContext();
        _authorizationServiceMock = new Mock<IAuthorizationService>();
        _emailServiceMock = new Mock<IEmailService>();
        _emailServiceMock
            .Setup(s => s.SendClubAssignmentConsentRequestAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _service = new ContactAssignmentConsentService(
            _dbContext,
            _authorizationServiceMock.Object,
            _emailServiceMock.Object,
            new Mock<IAuditLogRepository>().Object,
            new Mock<ILogger<ContactAssignmentConsentService>>().Object);

        _assignedUser = new User { Id = Guid.NewGuid(), Email = "neu@verein.example", FirstName = "Nora", LastName = "Neu" };
        _assigner = new User { Id = Guid.NewGuid(), Email = "vorstand@verein.example", FirstName = "Vera", LastName = "Vorstand" };
        _club = new Club { Id = Guid.NewGuid(), Name = "TSV Test" };
        _dbContext.AddRange(_assignedUser, _assigner, _club);
        _dbContext.SaveChanges();
    }

    [TestCleanup]
    public void Cleanup() => _dbContext.Dispose();

    private async Task<ContactAssignment> AddAssignmentAsync(bool confirmed, DateTime? lastConsentRequestAt = null)
    {
        var assignment = new ContactAssignment
        {
            Id = Guid.NewGuid(),
            UserId = _assignedUser.Id,
            ClubId = _club.Id,
            CanEdit = true,
            AssignedById = _assigner.Id,
            ConfirmedAt = confirmed ? DateTime.UtcNow : null,
            Visibility = confirmed ? VisibilityLevel.AllUsers : VisibilityLevel.Internal,
            LastConsentRequestAt = lastConsentRequestAt
        };
        _dbContext.ContactAssignments.Add(assignment);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
        return assignment;
    }

    [TestMethod]
    public async Task ConfirmAsync_PendingAssignment_SetsConsentAndVisibility()
    {
        var assignment = await AddAssignmentAsync(confirmed: false);

        var result = await _service.ConfirmAsync(_assignedUser.Id, assignment.Id, VisibilityLevel.Public, null);

        Assert.IsTrue(result.Success);
        var stored = await _dbContext.ContactAssignments.AsNoTracking().SingleAsync(a => a.Id == assignment.Id);
        Assert.IsNotNull(stored.ConfirmedAt);
        Assert.AreEqual(VisibilityLevel.Public, stored.Visibility);
    }

    [TestMethod]
    public async Task ConfirmAsync_WithoutContextEmailVisibility_AddressFollowsTheAssignment()
    {
        var assignment = await AddAssignmentAsync(confirmed: false);

        await _service.ConfirmAsync(_assignedUser.Id, assignment.Id, VisibilityLevel.AllUsers, null);

        var stored = await _dbContext.ContactAssignments.AsNoTracking().SingleAsync(a => a.Id == assignment.Id);
        Assert.AreEqual(VisibilityLevel.AllUsers, stored.ContextEmailVisibility);
    }

    [TestMethod]
    public async Task ConfirmAsync_StricterContextEmailVisibility_IsStoredSeparately()
    {
        var assignment = await AddAssignmentAsync(confirmed: false);

        await _service.ConfirmAsync(_assignedUser.Id, assignment.Id, VisibilityLevel.Public, VisibilityLevel.Internal);

        var stored = await _dbContext.ContactAssignments.AsNoTracking().SingleAsync(a => a.Id == assignment.Id);
        Assert.AreEqual(VisibilityLevel.Public, stored.Visibility);
        Assert.AreEqual(VisibilityLevel.Internal, stored.ContextEmailVisibility);
    }

    [TestMethod]
    public async Task ConfirmAsync_WiderContextEmailVisibility_IsCappedAtTheAssignment()
    {
        var assignment = await AddAssignmentAsync(confirmed: false);

        await _service.ConfirmAsync(_assignedUser.Id, assignment.Id, VisibilityLevel.AllUsers, VisibilityLevel.Public);

        var stored = await _dbContext.ContactAssignments.AsNoTracking().SingleAsync(a => a.Id == assignment.Id);
        Assert.AreEqual(VisibilityLevel.AllUsers, stored.ContextEmailVisibility, "The address must not be more visible than the assignment.");
    }

    [TestMethod]
    public async Task ConfirmAsync_ForeignAssignment_NotFound()
    {
        var assignment = await AddAssignmentAsync(confirmed: false);

        var result = await _service.ConfirmAsync(_assigner.Id, assignment.Id, VisibilityLevel.Public, null);

        Assert.AreEqual(ContactAssignmentConsentStatus.NotFound, result.Status);
        Assert.IsNull((await _dbContext.ContactAssignments.AsNoTracking().SingleAsync(a => a.Id == assignment.Id)).ConfirmedAt);
    }

    [TestMethod]
    public async Task ConfirmAsync_AlreadyConfirmed_Conflict()
    {
        var assignment = await AddAssignmentAsync(confirmed: true);

        var result = await _service.ConfirmAsync(_assignedUser.Id, assignment.Id, VisibilityLevel.Public, null);

        Assert.AreEqual(ContactAssignmentConsentStatus.Conflict, result.Status);
    }

    [TestMethod]
    public async Task DeclineAsync_PendingAssignment_RemovesItAndNotifiesAssigner()
    {
        var assignment = await AddAssignmentAsync(confirmed: false);

        var result = await _service.DeclineAsync(_assignedUser.Id, assignment.Id);

        Assert.IsTrue(result.Success);
        Assert.IsFalse(await _dbContext.ContactAssignments.AnyAsync(a => a.Id == assignment.Id));
        _emailServiceMock.Verify(s => s.SendAssignmentDeclinedNotificationAsync(
            _assigner.Email, _assigner.FullName, _assignedUser.FullName, _club.Name, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task DeclineAsync_SelfAssignment_NotifiesNobody()
    {
        // An editor of the club must not be informed when users decline their own self-assignment
        _dbContext.ContactAssignments.Add(new ContactAssignment
        {
            Id = Guid.NewGuid(), UserId = _assigner.Id, ClubId = _club.Id, CanEdit = true, ConfirmedAt = DateTime.UtcNow
        });
        var assignment = new ContactAssignment
        {
            Id = Guid.NewGuid(), UserId = _assignedUser.Id, ClubId = _club.Id, AssignedById = _assignedUser.Id
        };
        _dbContext.ContactAssignments.Add(assignment);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        var result = await _service.DeclineAsync(_assignedUser.Id, assignment.Id);

        Assert.IsTrue(result.Success);
        _emailServiceMock.Verify(s => s.SendAssignmentDeclinedNotificationAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task DeclineAsync_UnknownAssigner_NotifiesConfirmedEditors()
    {
        _dbContext.ContactAssignments.Add(new ContactAssignment
        {
            Id = Guid.NewGuid(), UserId = _assigner.Id, ClubId = _club.Id, CanEdit = true, ConfirmedAt = DateTime.UtcNow
        });
        var assignment = new ContactAssignment { Id = Guid.NewGuid(), UserId = _assignedUser.Id, ClubId = _club.Id, AssignedById = null };
        _dbContext.ContactAssignments.Add(assignment);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        await _service.DeclineAsync(_assignedUser.Id, assignment.Id);

        _emailServiceMock.Verify(s => s.SendAssignmentDeclinedNotificationAsync(
            _assigner.Email, It.IsAny<string>(), It.IsAny<string>(), _club.Name, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task DeclineAsync_ConfirmedAssignment_Conflict()
    {
        var assignment = await AddAssignmentAsync(confirmed: true);

        var result = await _service.DeclineAsync(_assignedUser.Id, assignment.Id);

        Assert.AreEqual(ContactAssignmentConsentStatus.Conflict, result.Status);
        Assert.IsTrue(await _dbContext.ContactAssignments.AnyAsync(a => a.Id == assignment.Id));
    }

    [TestMethod]
    public async Task SendReminderAsync_Editor_SendsAndRateLimits()
    {
        var assignment = await AddAssignmentAsync(confirmed: false);
        _authorizationServiceMock.Setup(a => a.CanEditClubAsync(_assigner.Id, _club.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var first = await _service.SendReminderAsync(_assigner.Id, assignment.Id);
        _dbContext.ChangeTracker.Clear();
        var second = await _service.SendReminderAsync(_assigner.Id, assignment.Id);

        Assert.IsTrue(first.Success);
        Assert.AreEqual(ContactAssignmentConsentStatus.TooManyRequests, second.Status);
        _emailServiceMock.Verify(s => s.SendClubAssignmentConsentRequestAsync(
            _assignedUser.Email, It.IsAny<string>(), _club.Name, assignment.Id, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task SendReminderAsync_WithoutEditRights_Forbidden()
    {
        var assignment = await AddAssignmentAsync(confirmed: false);

        var result = await _service.SendReminderAsync(Guid.NewGuid(), assignment.Id);

        Assert.AreEqual(ContactAssignmentConsentStatus.Forbidden, result.Status);
    }

    [TestMethod]
    public async Task SendReminderAsync_ConfirmedAssignment_Conflict()
    {
        var assignment = await AddAssignmentAsync(confirmed: true);
        _authorizationServiceMock.Setup(a => a.CanEditClubAsync(_assigner.Id, _club.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _service.SendReminderAsync(_assigner.Id, assignment.Id);

        Assert.AreEqual(ContactAssignmentConsentStatus.Conflict, result.Status);
    }

    [TestMethod]
    public async Task ConfirmAsync_UndefinedVisibility_IsRejected()
    {
        var assignment = await AddAssignmentAsync(confirmed: false);

        var result = await _service.ConfirmAsync(_assignedUser.Id, assignment.Id, (VisibilityLevel)999, null);

        Assert.AreEqual(ContactAssignmentConsentStatus.Conflict, result.Status);
    }

    [TestMethod]
    public async Task ConfirmAsync_AssignmentOfOtherUser_IsNotFound()
    {
        var assignment = await AddAssignmentAsync(confirmed: false);

        var result = await _service.ConfirmAsync(_assigner.Id, assignment.Id, VisibilityLevel.Public, null);

        Assert.AreEqual(ContactAssignmentConsentStatus.NotFound, result.Status);
    }

    [TestMethod]
    public async Task SendReminderAsync_UnknownAssignment_IsNotFound()
    {
        var result = await _service.SendReminderAsync(_assigner.Id, Guid.NewGuid());

        Assert.AreEqual(ContactAssignmentConsentStatus.NotFound, result.Status);
    }

    [TestMethod]
    public async Task SendReminderAsync_MailNotSent_ReportsConflictAndKeepsTimestamp()
    {
        var assignment = await AddAssignmentAsync(confirmed: false);
        _authorizationServiceMock.Setup(a => a.CanEditClubAsync(_assigner.Id, _club.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _emailServiceMock
            .Setup(s => s.SendClubAssignmentConsentRequestAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _service.SendReminderAsync(_assigner.Id, assignment.Id);

        Assert.AreEqual(ContactAssignmentConsentStatus.Conflict, result.Status);
        var stored = await _dbContext.ContactAssignments.AsNoTracking().SingleAsync(a => a.Id == assignment.Id);
        Assert.IsNull(stored.LastConsentRequestAt);
    }

    [TestMethod]
    public async Task DepartmentAssignment_ReminderAndDecline_UseDepartmentPath()
    {
        var department = new Department { Id = Guid.NewGuid(), Name = "Handball", ClubId = _club.Id };
        _dbContext.Departments.Add(department);
        var assignment = new ContactAssignment
        {
            Id = Guid.NewGuid(), UserId = _assignedUser.Id, DepartmentId = department.Id, AssignedById = _assigner.Id, Visibility = VisibilityLevel.Internal
        };
        _dbContext.ContactAssignments.Add(assignment);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
        _authorizationServiceMock.Setup(a => a.CanEditDepartmentAsync(_assigner.Id, department.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _emailServiceMock
            .Setup(s => s.SendDepartmentAssignmentConsentRequestAsync(
                _assignedUser.Email, It.IsAny<string>(), "Handball", "TSV Test", assignment.Id, false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _emailServiceMock
            .Setup(s => s.SendAssignmentDeclinedNotificationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMTP down"));

        var reminder = await _service.SendReminderAsync(_assigner.Id, assignment.Id);
        _dbContext.ChangeTracker.Clear();
        var decline = await _service.DeclineAsync(_assignedUser.Id, assignment.Id);

        Assert.AreEqual(ContactAssignmentConsentStatus.Success, reminder.Status);
        Assert.AreEqual(ContactAssignmentConsentStatus.Success, decline.Status, "A failed notification does not undo the decline");
        _emailServiceMock.Verify(s => s.SendAssignmentDeclinedNotificationAsync(
            _assigner.Email, It.IsAny<string>(), It.IsAny<string>(), "TSV Test › Handball", It.IsAny<CancellationToken>()), Times.Once);
        Assert.IsFalse(await _dbContext.ContactAssignments.AnyAsync(a => a.Id == assignment.Id));
    }
}
