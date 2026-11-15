using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Infrastructure.Data;
using Igevo.Vernetzt.Infrastructure.Services;
using Igevo.Vernetzt.Tests.TestInfrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace Igevo.Vernetzt.Tests.Services;

[TestClass]
public class PendingItemsServiceTests
{
    private VernetztDbContext _dbContext = null!;
    private PendingItemsService _service = null!;

    private User _manager = null!;
    private User _member = null!;
    private Club _club = null!;
    private Department _department = null!;
    private Department _group = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbContext = SqliteTestDatabase.CreateContext();
        _service = new PendingItemsService(
            _dbContext,
            new Mock<IAuditLogRepository>().Object,
            new Mock<ILogger<PendingItemsService>>().Object);

        _manager = new User { Id = Guid.NewGuid(), Email = "manager@example.org", FirstName = "Mia", LastName = "Manager", Claims = [UserClaim.Manager] };
        _member = new User { Id = Guid.NewGuid(), Email = "mitglied@example.org", FirstName = "Max", LastName = "Mitglied" };
        _club = new Club { Id = Guid.NewGuid(), Name = "TSV Test" };
        _department = new Department { Id = Guid.NewGuid(), Name = "Handball", ClubId = _club.Id };
        _group = new Department { Id = Guid.NewGuid(), Name = "Bambini", ClubId = _club.Id, ParentDepartmentId = _department.Id };
        _dbContext.AddRange(_manager, _member, _club, _department, _group);
        _dbContext.SaveChanges();
    }

    [TestCleanup]
    public void Cleanup() => _dbContext.Dispose();

    private InvitationToken AddInvitation(string email, bool used = false, bool revoked = false, int expiresInDays = 7)
    {
        var invitation = new InvitationToken
        {
            Id = Guid.NewGuid(),
            Email = email,
            Token = Guid.NewGuid().ToString("N"),
            InvitedById = _manager.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(expiresInDays),
            IsUsed = used,
            IsRevoked = revoked,
            DepartmentId = _group.Id,
            ContextEmail = "bambini@verein.example"
        };
        _dbContext.InvitationTokens.Add(invitation);
        return invitation;
    }

    /// <summary>Adds an assignment of the member to the club, or to the given department or group (one per organization).</summary>
    private ContactAssignment AddAssignment(bool confirmed, Department? department = null, string? contextEmail = null, bool contextEmailVerified = false)
    {
        var assignment = new ContactAssignment
        {
            Id = Guid.NewGuid(),
            UserId = _member.Id,
            ClubId = department is null ? _club.Id : null,
            DepartmentId = department?.Id,
            AssignedById = _manager.Id,
            ConfirmedAt = confirmed ? DateTime.UtcNow : null,
            ContextEmail = contextEmail,
            ContextEmailNormalized = contextEmail?.ToUpperInvariant(),
            ContextEmailVerified = contextEmailVerified
        };
        _dbContext.ContactAssignments.Add(assignment);
        return assignment;
    }

    [TestMethod]
    public async Task GetOverviewAsync_ReturnsOnlyOpenInvitations_WithOrganizationPath()
    {
        var open = AddInvitation("offen@example.org");
        AddInvitation("angenommen@example.org", used: true);
        AddInvitation("widerrufen@example.org", revoked: true);
        AddInvitation("abgelaufen@example.org", expiresInDays: -1);
        await _dbContext.SaveChangesAsync();

        var overview = await _service.GetOverviewAsync(_manager.Id);

        Assert.HasCount(1, overview.Invitations);
        var item = overview.Invitations[0];
        Assert.AreEqual(open.Id, item.Id);
        Assert.AreEqual("offen@example.org", item.Email);
        Assert.AreEqual("Mia Manager", item.InvitedByName);
        Assert.AreEqual("TSV Test", item.ClubName);
        Assert.AreEqual(_club.Id, item.ClubId);
        Assert.AreEqual("Handball", item.ParentDepartmentName);
        Assert.AreEqual("Bambini", item.DepartmentName);
        Assert.AreEqual("bambini@verein.example", item.ContextEmail);
    }

    [TestMethod]
    public async Task GetOverviewAsync_ReturnsPendingAssignmentsAndUnconfirmedContextEmails()
    {
        var pending = AddAssignment(confirmed: false);
        AddAssignment(confirmed: true, _department);
        var unverified = AddAssignment(confirmed: true, _group, contextEmail: "bambini@verein.example");
        var otherClub = new Club { Id = Guid.NewGuid(), Name = "SV Nachbar" };
        _dbContext.Clubs.Add(otherClub);
        _dbContext.ContactAssignments.Add(new ContactAssignment
        {
            Id = Guid.NewGuid(), UserId = _member.Id, ClubId = otherClub.Id, ConfirmedAt = DateTime.UtcNow,
            ContextEmail = "kasse@nachbar.example", ContextEmailNormalized = "KASSE@NACHBAR.EXAMPLE", ContextEmailVerified = true
        });
        _dbContext.ContactAssignmentEmailVerificationTokens.Add(new ContactAssignmentEmailVerificationToken
        {
            Id = Guid.NewGuid(),
            ContactAssignmentId = unverified.Id,
            TokenHash = "hash",
            EmailNormalizedSnapshot = "BAMBINI@VEREIN.EXAMPLE",
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            CreatedAt = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc)
        });
        await _dbContext.SaveChangesAsync();

        var overview = await _service.GetOverviewAsync(_manager.Id);

        Assert.HasCount(1, overview.Assignments);
        Assert.AreEqual(pending.Id, overview.Assignments[0].AssignmentId);
        Assert.AreEqual("Max Mitglied", overview.Assignments[0].UserName);
        Assert.AreEqual("Mia Manager", overview.Assignments[0].AssignedByName);
        Assert.AreEqual("TSV Test", overview.Assignments[0].ClubName);

        Assert.HasCount(1, overview.ContextEmails);
        var email = overview.ContextEmails[0];
        Assert.AreEqual(unverified.Id, email.AssignmentId);
        Assert.AreEqual("bambini@verein.example", email.ContextEmail);
        Assert.AreEqual("TSV Test", email.ClubName);
        Assert.AreEqual(_club.Id, email.ClubId);
        Assert.AreEqual("Handball", email.ParentDepartmentName);
        Assert.AreEqual("Bambini", email.DepartmentName);
        Assert.IsTrue(email.IsAssignmentConfirmed);
        Assert.AreEqual(new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc), email.LastVerificationSentAt);
    }

    [TestMethod]
    public async Task GetOverviewAsync_RegularUser_Throws()
    {
        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => _service.GetOverviewAsync(_member.Id));
    }

    [TestMethod]
    public async Task RevokeInvitationAsync_OpenInvitation_RevokesIt()
    {
        var invitation = AddInvitation("offen@example.org");
        await _dbContext.SaveChangesAsync();

        var revoked = await _service.RevokeInvitationAsync(_manager.Id, invitation.Id);

        Assert.IsTrue(revoked);
        _dbContext.ChangeTracker.Clear();
        var stored = await _dbContext.InvitationTokens.SingleAsync(i => i.Id == invitation.Id);
        Assert.IsTrue(stored.IsRevoked);
        Assert.IsNotNull(stored.RevokedAt);
        Assert.IsEmpty((await _service.GetOverviewAsync(_manager.Id)).Invitations);
    }

    [TestMethod]
    public async Task RevokeInvitationAsync_UsedInvitation_ReturnsFalse()
    {
        var invitation = AddInvitation("angenommen@example.org", used: true);
        await _dbContext.SaveChangesAsync();

        Assert.IsFalse(await _service.RevokeInvitationAsync(_manager.Id, invitation.Id));
    }

    [TestMethod]
    public async Task RevokeInvitationAsync_RegularUser_Throws()
    {
        var invitation = AddInvitation("offen@example.org");
        await _dbContext.SaveChangesAsync();

        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => _service.RevokeInvitationAsync(_member.Id, invitation.Id));
    }
}
