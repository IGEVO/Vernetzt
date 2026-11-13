using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Data;
using Igevo.Vernetzt.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Igevo.Vernetzt.Tests.TestInfrastructure;

namespace Igevo.Vernetzt.Tests.Services;

[TestClass]
public class RecipientResolutionServiceTests
{
    private VernetztDbContext _dbContext = null!;
    private Mock<IAuthorizationService> _authorizationServiceMock = null!;
    private RecipientResolutionService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbContext = SqliteTestDatabase.CreateContext();

        _authorizationServiceMock = new Mock<IAuthorizationService>();
        _authorizationServiceMock
            .Setup(a => a.CanAccessClubAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _authorizationServiceMock
            .Setup(a => a.CanAccessDepartmentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _service = new RecipientResolutionService(
            _dbContext,
            _authorizationServiceMock.Object,
            new Mock<ILogger<RecipientResolutionService>>().Object);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _dbContext.Dispose();
    }

    [TestMethod]
    public async Task ResolveForSendAsync_AssignmentRecipients_DeduplicatesByEffectiveEmail()
    {
        var requesterId = Guid.NewGuid();
        var user1 = CreateUser("owner.one@example.com");
        var user2 = CreateUser("owner.two@example.com");
        var clubA = new Club { Id = Guid.NewGuid(), Name = "Club A" };
        var clubB = new Club { Id = Guid.NewGuid(), Name = "Club B" };
        var department = new Department { Id = Guid.NewGuid(), Name = "Department A", ClubId = clubA.Id };

        var sharedContextEmail = "vorstand@club-a.de";
        var assignment1 = new ContactAssignment
        {
            ConfirmedAt = DateTime.UtcNow,
            Id = Guid.NewGuid(),
            UserId = user1.Id,
            ClubId = clubA.Id,
            ContextEmail = sharedContextEmail,
            ContextEmailNormalized = EmailAddressHelper.Normalize(sharedContextEmail),
            ContextEmailVerified = true,
            Visibility = VisibilityLevel.AllUsers
        };
        var assignment2 = new ContactAssignment
        {
            ConfirmedAt = DateTime.UtcNow,
            Id = Guid.NewGuid(),
            UserId = user1.Id,
            DepartmentId = department.Id,
            ContextEmail = sharedContextEmail.ToUpperInvariant(),
            ContextEmailNormalized = EmailAddressHelper.Normalize(sharedContextEmail.ToUpperInvariant()),
            ContextEmailVerified = true,
            Visibility = VisibilityLevel.AllUsers
        };
        var assignment3 = new ContactAssignment
        {
            ConfirmedAt = DateTime.UtcNow,
            Id = Guid.NewGuid(),
            UserId = user2.Id,
            ClubId = clubB.Id
        };

        await _dbContext.Users.AddRangeAsync(user1, user2);
        await _dbContext.Clubs.AddRangeAsync(clubA, clubB);
        await _dbContext.Departments.AddAsync(department);
        await _dbContext.ContactAssignments.AddRangeAsync(assignment1, assignment2, assignment3);
        await _dbContext.SaveChangesAsync();

        var result = await _service.ResolveForSendAsync(
            requesterId,
            new[] { assignment1.Id, assignment2.Id, assignment3.Id },
            Array.Empty<Guid>());

        Assert.IsTrue(result.Success);
        Assert.IsFalse(result.Forbidden);
        Assert.AreEqual(2, result.RecipientEmails.Count);
        CollectionAssert.Contains(result.RecipientEmails, "vorstand@club-a.de");
        CollectionAssert.Contains(result.RecipientEmails, "owner.two@example.com");
        Assert.AreEqual(2, result.RecipientDisplayItems.Count);
        Assert.IsTrue(result.RecipientDisplayItems.Any(name => name.Contains("Club A") || name.Contains("Club B")));
    }

    [TestMethod]
    public async Task ResolveForSendAsync_AssignmentRecipients_WithMissingId_ReturnsForbidden()
    {
        var requesterId = Guid.NewGuid();
        var user = CreateUser("user@example.com");
        var club = new Club { Id = Guid.NewGuid(), Name = "Club" };
        var assignment = new ContactAssignment
        {
            ConfirmedAt = DateTime.UtcNow,
            Id = Guid.NewGuid(),
            UserId = user.Id,
            ClubId = club.Id
        };
        var missingId = Guid.NewGuid();

        await _dbContext.Users.AddAsync(user);
        await _dbContext.Clubs.AddAsync(club);
        await _dbContext.ContactAssignments.AddAsync(assignment);
        await _dbContext.SaveChangesAsync();

        var result = await _service.ResolveForSendAsync(
            requesterId,
            new[] { assignment.Id, missingId },
            Array.Empty<Guid>());

        Assert.IsTrue(result.Forbidden);
        CollectionAssert.Contains(result.ForbiddenAssignmentIds, missingId);
    }

    [TestMethod]
    public async Task ResolveForSendAsync_PendingAssignment_CannotBeAddressed()
    {
        var requesterId = Guid.NewGuid();
        var user = CreateUser("pending@example.com");
        var club = new Club { Id = Guid.NewGuid(), Name = "Club" };
        var pending = new ContactAssignment { Id = Guid.NewGuid(), UserId = user.Id, ClubId = club.Id, ConfirmedAt = null };

        await _dbContext.Users.AddAsync(user);
        await _dbContext.Clubs.AddAsync(club);
        await _dbContext.ContactAssignments.AddAsync(pending);
        await _dbContext.SaveChangesAsync();

        var result = await _service.ResolveForSendAsync(requesterId, new[] { pending.Id }, Array.Empty<Guid>());

        Assert.IsTrue(result.Forbidden, "Mails reach an assignment only after the user consented to it.");
        CollectionAssert.Contains(result.ForbiddenAssignmentIds, pending.Id);
    }

    [TestMethod]
    public async Task ResolveForSendAsync_AssignmentRecipients_WithUnauthorizedId_ReturnsForbidden()
    {
        var requesterId = Guid.NewGuid();
        var user = CreateUser("user@example.com");
        var allowedClubId = Guid.NewGuid();
        var forbiddenClubId = Guid.NewGuid();
        var allowedAssignment = new ContactAssignment
        {
            ConfirmedAt = DateTime.UtcNow,
            Id = Guid.NewGuid(),
            UserId = user.Id,
            ClubId = allowedClubId
        };
        var forbiddenAssignment = new ContactAssignment
        {
            ConfirmedAt = DateTime.UtcNow,
            Id = Guid.NewGuid(),
            UserId = user.Id,
            ClubId = forbiddenClubId
        };

        await _dbContext.Users.AddAsync(user);
        await _dbContext.Clubs.AddRangeAsync(
            new Club { Id = allowedClubId, Name = "Allowed Club" },
            new Club { Id = forbiddenClubId, Name = "Forbidden Club" });
        await _dbContext.ContactAssignments.AddRangeAsync(allowedAssignment, forbiddenAssignment);
        await _dbContext.SaveChangesAsync();

        _authorizationServiceMock
            .Setup(a => a.CanAccessClubAsync(requesterId, forbiddenClubId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _service.ResolveForSendAsync(
            requesterId,
            new[] { allowedAssignment.Id, forbiddenAssignment.Id },
            Array.Empty<Guid>());

        Assert.IsTrue(result.Forbidden);
        CollectionAssert.Contains(result.ForbiddenAssignmentIds, forbiddenAssignment.Id);
    }

    [TestMethod]
    public async Task ResolveForSendAsync_UserFallback_DeduplicatesRepeatedUsers()
    {
        // Primary emails are stored lower-case with a unique index, so two users cannot share one;
        // the same user requested twice must still yield a single recipient.
        var requesterId = Guid.NewGuid();
        var user1 = CreateUser("DUP@EXAMPLE.COM");

        await _dbContext.Users.AddAsync(user1);
        await _dbContext.SaveChangesAsync();

        var result = await _service.ResolveForSendAsync(
            requesterId,
            Array.Empty<Guid>(),
            new[] { user1.Id, user1.Id, Guid.NewGuid() });

        Assert.IsTrue(result.Success);
        Assert.IsFalse(result.Forbidden);
        Assert.AreEqual(1, result.RecipientEmails.Count);
        Assert.AreEqual("dup@example.com", result.RecipientEmails.Single());
        Assert.AreEqual(1, result.RecipientDisplayItems.Count);
        Assert.IsFalse(result.RecipientDisplayItems.Single().Contains("@"));
    }

    [TestMethod]
    public async Task ResolveForSendAsync_MixedAssignmentAndUserRecipients_MergesAndDeduplicates()
    {
        var requesterId = Guid.NewGuid();
        var assignedUser = CreateUser("vorstand@example.com");
        var unassignedUser = CreateUser("team@example.com");
        var club = new Club { Id = Guid.NewGuid(), Name = "Club" };
        var assignment = new ContactAssignment
        {
            ConfirmedAt = DateTime.UtcNow,
            Id = Guid.NewGuid(),
            UserId = assignedUser.Id,
            ClubId = club.Id
        };

        await _dbContext.Users.AddRangeAsync(assignedUser, unassignedUser);
        await _dbContext.Clubs.AddAsync(club);
        await _dbContext.ContactAssignments.AddAsync(assignment);
        await _dbContext.SaveChangesAsync();

        var result = await _service.ResolveForSendAsync(
            requesterId,
            new[] { assignment.Id },
            new[] { unassignedUser.Id });

        Assert.IsTrue(result.Success);
        Assert.IsFalse(result.Forbidden);
        Assert.AreEqual(2, result.RecipientEmails.Count);
        CollectionAssert.Contains(result.RecipientEmails, "vorstand@example.com");
        CollectionAssert.Contains(result.RecipientEmails, "team@example.com");
        Assert.AreEqual(2, result.RecipientDisplayItems.Count);
    }

    [TestMethod]
    public async Task ResolveDisplayItemsByEmailAsync_ContextEmailAndUserEmail_ReturnsNamesWithoutAddresses()
    {
        var user = CreateUser("vorstand@example.com");
        var club = new Club { Id = Guid.NewGuid(), Name = "TSV Blau" };
        var department = new Department { Id = Guid.NewGuid(), Name = "Jugend", ClubId = club.Id };
        var assignment = new ContactAssignment
        {
            ConfirmedAt = DateTime.UtcNow,
            Id = Guid.NewGuid(),
            UserId = user.Id,
            DepartmentId = department.Id,
            ContextEmail = "jugend@tsv-blau.de",
            ContextEmailNormalized = "jugend@tsv-blau.de",
            ContextEmailVerified = true
        };

        await _dbContext.Users.AddAsync(user);
        await _dbContext.Clubs.AddAsync(club);
        await _dbContext.Departments.AddAsync(department);
        await _dbContext.ContactAssignments.AddAsync(assignment);
        await _dbContext.SaveChangesAsync();

        var displayItems = await _service.ResolveDisplayItemsByEmailAsync(new[]
        {
            "jugend@tsv-blau.de",
            "vorstand@example.com"
        });

        Assert.AreEqual(2, displayItems.Count);
        Assert.IsTrue(displayItems[0].Contains("Test User"));
        Assert.IsTrue(displayItems[0].Contains("Jugend"));
        Assert.IsTrue(displayItems[0].Contains("TSV Blau"));
        Assert.IsFalse(displayItems[1].Contains("@"));
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
