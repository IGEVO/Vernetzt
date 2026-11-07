using Igevo.Vernetzt.Core.Configuration;
using Igevo.Vernetzt.Core.DTOs.Users;
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

[TestClass]
public class UserServiceContextEmailTests
{
    private VernetztDbContext _dbContext = null!;
    private Mock<IUserRepository> _userRepositoryMock = null!;
    private Mock<IClubRepository> _clubRepositoryMock = null!;
    private Mock<IDepartmentRepository> _departmentRepositoryMock = null!;
    private Mock<IContactPositionRepository> _contactPositionRepositoryMock = null!;
    private Mock<IAuthorizationService> _authorizationServiceMock = null!;
    private Mock<IAuditLogRepository> _auditLogRepositoryMock = null!;
    private Mock<IEmailService> _emailServiceMock = null!;
    private Mock<IInvitationTokenRepository> _invitationTokenRepositoryMock = null!;
    private Mock<IAppSettingsService> _appSettingsServiceMock = null!;
    private UserService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbContext = SqliteTestDatabase.CreateContext();

        _userRepositoryMock = new Mock<IUserRepository>();
        _clubRepositoryMock = new Mock<IClubRepository>();
        _departmentRepositoryMock = new Mock<IDepartmentRepository>();
        _contactPositionRepositoryMock = new Mock<IContactPositionRepository>();
        _authorizationServiceMock = new Mock<IAuthorizationService>();
        _auditLogRepositoryMock = new Mock<IAuditLogRepository>();
        _emailServiceMock = new Mock<IEmailService>();
        _invitationTokenRepositoryMock = new Mock<IInvitationTokenRepository>();
        _appSettingsServiceMock = new Mock<IAppSettingsService>();

        _authorizationServiceMock
            .Setup(a => a.CanAccessClubAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _authorizationServiceMock
            .Setup(a => a.CanAccessDepartmentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => _dbContext.Users.FirstOrDefault(u => u.Id == id));

        _emailServiceMock
            .Setup(e => e.SendEmailAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _emailServiceMock
            .Setup(e => e.SendContactAssignmentEmailVerificationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _auditLogRepositoryMock
            .Setup(a => a.LogAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<AuditAction>(),
                It.IsAny<Guid>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _auditLogRepositoryMock
            .Setup(a => a.LogAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var appSettings = Options.Create(new AppSettings
        {
            SystemAdminEmail = "admin@test.local",
            BaseUrl = "https://vernetzt.test.local"
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
            new Mock<ILogger<UserService>>().Object);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _dbContext.Dispose();
    }

    [TestMethod]
    public async Task UpdateMyContactAssignmentEmailAsync_NewEmail_IssuesVerificationToken()
    {
        var user = CreateUser("member@test.local");
        var club = new Club { Id = Guid.NewGuid(), Name = "Club A" };
        var assignment = new ContactAssignment
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            ClubId = club.Id
        };

        await _dbContext.Users.AddAsync(user);
        await _dbContext.Clubs.AddAsync(club);
        await _dbContext.ContactAssignments.AddAsync(assignment);
        await _dbContext.SaveChangesAsync();

        var response = await _service.UpdateMyContactAssignmentEmailAsync(
            user.Id,
            assignment.Id,
            new UpdateMyContactAssignmentEmailDto
            {
                ContextEmail = "trainer@club-a.de",
                ContextEmailVisibility = VisibilityLevel.Internal
            });

        Assert.IsTrue(response.Success);
        Assert.IsTrue(response.VerificationEmailSent);

        var updatedAssignment = await _dbContext.ContactAssignments.SingleAsync(a => a.Id == assignment.Id);
        Assert.AreEqual("trainer@club-a.de", updatedAssignment.ContextEmail);
        Assert.AreEqual("trainer@club-a.de", updatedAssignment.ContextEmailNormalized);
        Assert.IsFalse(updatedAssignment.ContextEmailVerified);
        Assert.AreEqual(VisibilityLevel.Internal, updatedAssignment.ContextEmailVisibility);
        Assert.AreEqual(1, await _dbContext.ContactAssignmentEmailVerificationTokens.CountAsync());
    }

    [TestMethod]
    public async Task UpdateMyContactAssignmentEmailAsync_PrimaryEmailInUse_ReturnsValidationError()
    {
        var user = CreateUser("member@test.local");
        var otherUser = CreateUser("shared-mail@test.local");
        var club = new Club { Id = Guid.NewGuid(), Name = "Club Primary Check" };
        var assignment = new ContactAssignment
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            ClubId = club.Id
        };

        await _dbContext.Users.AddRangeAsync(user, otherUser);
        await _dbContext.Clubs.AddAsync(club);
        await _dbContext.ContactAssignments.AddAsync(assignment);
        await _dbContext.SaveChangesAsync();

        var response = await _service.UpdateMyContactAssignmentEmailAsync(
            user.Id,
            assignment.Id,
            new UpdateMyContactAssignmentEmailDto
            {
                ContextEmail = "shared-mail@test.local",
                ContextEmailVisibility = VisibilityLevel.Internal
            });

        Assert.IsFalse(response.Success);
        StringAssert.Contains(response.Message, "persönliche Anmeldeadresse");
        Assert.IsFalse(response.VerificationEmailSent);

        var unchangedAssignment = await _dbContext.ContactAssignments.SingleAsync(a => a.Id == assignment.Id);
        Assert.IsNull(unchangedAssignment.ContextEmail);
        Assert.IsNull(unchangedAssignment.ContextEmailNormalized);
        Assert.IsFalse(unchangedAssignment.ContextEmailVerified);
    }

    [TestMethod]
    public async Task UpdateMyContactAssignmentEmailAsync_OwnPrimaryEmail_ReturnsValidationError()
    {
        var user = CreateUser("member-primary@test.local");
        var club = new Club { Id = Guid.NewGuid(), Name = "Club Own Primary Check" };
        var assignment = new ContactAssignment
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            ClubId = club.Id
        };

        await _dbContext.Users.AddAsync(user);
        await _dbContext.Clubs.AddAsync(club);
        await _dbContext.ContactAssignments.AddAsync(assignment);
        await _dbContext.SaveChangesAsync();

        var response = await _service.UpdateMyContactAssignmentEmailAsync(
            user.Id,
            assignment.Id,
            new UpdateMyContactAssignmentEmailDto
            {
                ContextEmail = "member-primary@test.local",
                ContextEmailVisibility = VisibilityLevel.Internal
            });

        Assert.IsFalse(response.Success);
        StringAssert.Contains(response.Message, "persönliche Anmeldeadresse");
        Assert.IsFalse(response.VerificationEmailSent);

        var unchangedAssignment = await _dbContext.ContactAssignments.SingleAsync(a => a.Id == assignment.Id);
        Assert.IsNull(unchangedAssignment.ContextEmail);
        Assert.IsNull(unchangedAssignment.ContextEmailNormalized);
        Assert.IsFalse(unchangedAssignment.ContextEmailVerified);
    }

    [TestMethod]
    public async Task VerifyContactAssignmentEmailAsync_ValidToken_VerifiesAssignmentAndMarksTokenUsed()
    {
        var user = CreateUser("member@test.local");
        var club = new Club { Id = Guid.NewGuid(), Name = "Club B" };
        var assignment = new ContactAssignment
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            ClubId = club.Id,
            ContextEmail = "vorstand@club-b.de",
            ContextEmailNormalized = "vorstand@club-b.de",
            ContextEmailVerified = false
        };

        var rawToken = "context-email-token";
        var token = new ContactAssignmentEmailVerificationToken
        {
            Id = Guid.NewGuid(),
            ContactAssignmentId = assignment.Id,
            TokenHash = EmailAddressHelper.Sha256(rawToken),
            EmailNormalizedSnapshot = assignment.ContextEmailNormalized!,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            IsUsed = false
        };

        await _dbContext.Users.AddAsync(user);
        await _dbContext.Clubs.AddAsync(club);
        await _dbContext.ContactAssignments.AddAsync(assignment);
        await _dbContext.ContactAssignmentEmailVerificationTokens.AddAsync(token);
        await _dbContext.SaveChangesAsync();

        var result = await _service.VerifyContactAssignmentEmailAsync(rawToken);

        Assert.IsTrue(result.Success);
        var updatedAssignment = await _dbContext.ContactAssignments.SingleAsync(a => a.Id == assignment.Id);
        var updatedToken = await _dbContext.ContactAssignmentEmailVerificationTokens.SingleAsync(t => t.Id == token.Id);
        Assert.IsTrue(updatedAssignment.ContextEmailVerified);
        Assert.IsNotNull(updatedAssignment.ContextEmailVerifiedAt);
        Assert.IsTrue(updatedToken.IsUsed);
        Assert.IsNotNull(updatedToken.UsedAt);
    }

    [TestMethod]
    public async Task GetRecipientCandidatesAsync_UnverifiedContextEmail_UsesPrimaryFallbackWithBadge()
    {
        var user = CreateUser("member@test.local");
        var club = new Club { Id = Guid.NewGuid(), Name = "Club C" };
        var assignment = new ContactAssignment
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            ClubId = club.Id,
            ContextEmail = "team@club-c.de",
            ContextEmailNormalized = "team@club-c.de",
            ContextEmailVerified = false,
            ContextEmailVisibility = VisibilityLevel.Internal
        };

        await _dbContext.Users.AddAsync(user);
        await _dbContext.Clubs.AddAsync(club);
        await _dbContext.ContactAssignments.AddAsync(assignment);
        await _dbContext.SaveChangesAsync();

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var result = (await _service.GetRecipientCandidatesAsync(user.Id)).ToList();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("member@test.local", result[0].DisplayEmail);
        Assert.IsFalse(result[0].ContextEmailVerified);
        Assert.AreEqual("ContextUnverifiedHidden", result[0].EmailBadgeType);
    }

    [TestMethod]
    public async Task UpdateUserContactAssignmentEmailAsync_ManagerCanUpdateOtherUser()
    {
        var manager = CreateUser("manager@test.local");
        manager.Claims.Add(UserClaim.Manager);

        var member = CreateUser("member@test.local");
        var club = new Club { Id = Guid.NewGuid(), Name = "Club D" };
        var assignment = new ContactAssignment
        {
            Id = Guid.NewGuid(),
            UserId = member.Id,
            ClubId = club.Id
        };

        await _dbContext.Users.AddRangeAsync(manager, member);
        await _dbContext.Clubs.AddAsync(club);
        await _dbContext.ContactAssignments.AddAsync(assignment);
        await _dbContext.SaveChangesAsync();

        var response = await _service.UpdateUserContactAssignmentEmailAsync(
            manager.Id,
            member.Id,
            assignment.Id,
            new UpdateMyContactAssignmentEmailDto
            {
                ContextEmail = "vorstand@club-d.de",
                ContextEmailVisibility = VisibilityLevel.AllUsers
            });

        Assert.IsTrue(response.Success);
        Assert.IsTrue(response.VerificationEmailSent);

        var updatedAssignment = await _dbContext.ContactAssignments.SingleAsync(a => a.Id == assignment.Id);
        Assert.AreEqual("vorstand@club-d.de", updatedAssignment.ContextEmail);
        Assert.IsFalse(updatedAssignment.ContextEmailVerified);
    }

    [TestMethod]
    public async Task UpdateUserContactAssignmentEmailAsync_RegularUserThrowsUnauthorized()
    {
        var regularUser = CreateUser("regular@test.local");
        var member = CreateUser("member@test.local");
        var club = new Club { Id = Guid.NewGuid(), Name = "Club E" };
        var assignment = new ContactAssignment
        {
            Id = Guid.NewGuid(),
            UserId = member.Id,
            ClubId = club.Id
        };

        await _dbContext.Users.AddRangeAsync(regularUser, member);
        await _dbContext.Clubs.AddAsync(club);
        await _dbContext.ContactAssignments.AddAsync(assignment);
        await _dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.UpdateUserContactAssignmentEmailAsync(
                regularUser.Id,
                member.Id,
                assignment.Id,
                new UpdateMyContactAssignmentEmailDto
                {
                    ContextEmail = "kontakt@club-e.de",
                    ContextEmailVisibility = VisibilityLevel.Internal
                }));
    }

    private static User CreateUser(string email)
    {
        return new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            FirstName = "Test",
            LastName = "User",
            Claims = [],
            EmailVisibility = VisibilityLevel.AllUsers,
            PhoneVisibility = VisibilityLevel.AllUsers
        };
    }
}
