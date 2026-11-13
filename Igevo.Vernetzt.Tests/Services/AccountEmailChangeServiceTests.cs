using Igevo.Vernetzt.Core.Configuration;
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
using Microsoft.Extensions.Options;
using Moq;

namespace Igevo.Vernetzt.Tests.Services;

[TestClass]
public class AccountEmailChangeServiceTests
{
    private const string AdminEmail = "admin@test.local";

    private VernetztDbContext _dbContext = null!;
    private Mock<IEmailService> _emailServiceMock = null!;
    private AccountEmailChangeService _service = null!;
    private string? _lastConfirmUrl;

    [TestInitialize]
    public void Setup()
    {
        _dbContext = SqliteTestDatabase.CreateContext();
        _emailServiceMock = new Mock<IEmailService>();
        _emailServiceMock
            .Setup(e => e.SendAccountEmailChangeConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string _, string _, string url, CancellationToken _) => _lastConfirmUrl = url)
            .ReturnsAsync(true);
        _emailServiceMock
            .Setup(e => e.SendAccountEmailChangedNoticeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _service = new AccountEmailChangeService(
            _dbContext,
            _emailServiceMock.Object,
            new Mock<IAuditLogRepository>().Object,
            Options.Create(new AppSettings { SystemAdminEmail = AdminEmail, BaseUrl = "https://vernetzt.test.local" }),
            new Mock<ILogger<AccountEmailChangeService>>().Object);
    }

    [TestCleanup]
    public void Cleanup() => _dbContext.Dispose();

    [TestMethod]
    public async Task RequestAndConfirm_OwnAccount_ChangesEmailAndIdentityAccountAndNotifiesOldAddress()
    {
        var user = await AddUserAsync("old@test.local");

        var request = await _service.RequestChangeAsync(user.Id, user.Id, "  New@Test.Local ");

        Assert.AreEqual(AccountEmailChangeStatus.ConfirmationSent, request.Status);
        _emailServiceMock.Verify(e => e.SendAccountEmailChangeConfirmationAsync("new@test.local", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.AreEqual("old@test.local", (await ReloadUserAsync(user.Id)).Email, "address must not change before confirmation");

        var result = await _service.ConfirmChangeAsync(ExtractToken());

        Assert.IsTrue(result.Success, result.Message);
        _dbContext.ChangeTracker.Clear();
        Assert.AreEqual("new@test.local", (await ReloadUserAsync(user.Id)).Email);
        var account = await _dbContext.IdentityAccounts.SingleAsync(a => a.Id == user.Id);
        Assert.AreEqual("new@test.local", account.Email);
        Assert.AreEqual("NEW@TEST.LOCAL", account.NormalizedEmail);
        _emailServiceMock.Verify(e => e.SendAccountEmailChangedNoticeAsync("old@test.local", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task Confirm_TokenUsedTwice_SecondAttemptFails()
    {
        var user = await AddUserAsync("old@test.local");
        await _service.RequestChangeAsync(user.Id, user.Id, "new@test.local");
        var token = ExtractToken();

        Assert.IsTrue((await _service.ConfirmChangeAsync(token)).Success);
        Assert.IsFalse((await _service.ConfirmChangeAsync(token)).Success);
    }

    [TestMethod]
    public async Task Confirm_ExpiredToken_Fails()
    {
        var user = await AddUserAsync("old@test.local");
        await _service.RequestChangeAsync(user.Id, user.Id, "new@test.local");
        var stored = await _dbContext.AccountEmailChangeTokens.SingleAsync();
        stored.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await _dbContext.SaveChangesAsync();

        var result = await _service.ConfirmChangeAsync(ExtractToken());

        Assert.IsFalse(result.Success);
        Assert.AreEqual("old@test.local", (await ReloadUserAsync(user.Id)).Email);
    }

    [TestMethod]
    public async Task Request_NewRequest_InvalidatesPreviousLink()
    {
        var user = await AddUserAsync("old@test.local");
        await _service.RequestChangeAsync(user.Id, user.Id, "first@test.local");
        var firstToken = ExtractToken();
        await _service.RequestChangeAsync(user.Id, user.Id, "second@test.local");

        Assert.IsFalse((await _service.ConfirmChangeAsync(firstToken)).Success);
        Assert.IsTrue((await _service.ConfirmChangeAsync(ExtractToken())).Success);
        _dbContext.ChangeTracker.Clear();
        Assert.AreEqual("second@test.local", (await ReloadUserAsync(user.Id)).Email);
    }

    [TestMethod]
    public async Task Request_AddressOfOtherAccount_Conflict()
    {
        var user = await AddUserAsync("old@test.local");
        await AddUserAsync("taken@test.local");

        var result = await _service.RequestChangeAsync(user.Id, user.Id, "TAKEN@test.local");

        Assert.AreEqual(AccountEmailChangeStatus.Conflict, result.Status);
        _emailServiceMock.Verify(e => e.SendAccountEmailChangeConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Confirm_AddressTakenAfterRequest_Fails()
    {
        var user = await AddUserAsync("old@test.local");
        await _service.RequestChangeAsync(user.Id, user.Id, "new@test.local");
        await AddUserAsync("new@test.local");

        var result = await _service.ConfirmChangeAsync(ExtractToken());

        Assert.IsFalse(result.Success);
        Assert.AreEqual("old@test.local", (await ReloadUserAsync(user.Id)).Email);
    }

    [TestMethod]
    public async Task Request_ContextEmailOfOtherPerson_Conflict()
    {
        var user = await AddUserAsync("old@test.local");
        var other = await AddUserAsync("other@test.local");
        var club = new Club { Id = Guid.NewGuid(), Name = "Club" };
        _dbContext.Clubs.Add(club);
        _dbContext.ContactAssignments.Add(new ContactAssignment
        {
            Id = Guid.NewGuid(),
            UserId = other.Id,
            ClubId = club.Id,
            ContextEmail = "vorstand@club.de",
            ContextEmailNormalized = "vorstand@club.de",
            ContextEmailVerified = true
        });
        await _dbContext.SaveChangesAsync();

        var result = await _service.RequestChangeAsync(user.Id, user.Id, "vorstand@club.de");

        Assert.AreEqual(AccountEmailChangeStatus.Conflict, result.Status);
    }

    [TestMethod]
    public async Task Request_OpenInvitationForAddress_Conflict()
    {
        var user = await AddUserAsync("old@test.local");
        _dbContext.InvitationTokens.Add(new InvitationToken
        {
            Id = Guid.NewGuid(),
            Email = "invited@test.local",
            Token = "invitation-token",
            InvitedById = user.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(3)
        });
        await _dbContext.SaveChangesAsync();

        var result = await _service.RequestChangeAsync(user.Id, user.Id, "invited@test.local");

        Assert.AreEqual(AccountEmailChangeStatus.Conflict, result.Status);
    }

    [TestMethod]
    public async Task Request_BootstrapAdminAccount_Rejected()
    {
        var admin = await AddUserAsync(AdminEmail, UserClaim.SystemAdmin);

        var result = await _service.RequestChangeAsync(admin.Id, admin.Id, "new-admin@test.local");

        Assert.AreEqual(AccountEmailChangeStatus.Invalid, result.Status);
    }

    [TestMethod]
    public async Task Request_BootstrapAdminAddressAsNewAddress_Conflict()
    {
        var user = await AddUserAsync("old@test.local");

        var result = await _service.RequestChangeAsync(user.Id, user.Id, AdminEmail.ToUpperInvariant());

        Assert.AreEqual(AccountEmailChangeStatus.Conflict, result.Status);
    }

    [TestMethod]
    public async Task Request_SameAddress_Invalid()
    {
        var user = await AddUserAsync("old@test.local");

        var result = await _service.RequestChangeAsync(user.Id, user.Id, "OLD@test.local");

        Assert.AreEqual(AccountEmailChangeStatus.Invalid, result.Status);
    }

    [TestMethod]
    public async Task Request_InvalidAddress_Invalid()
    {
        var user = await AddUserAsync("old@test.local");

        var result = await _service.RequestChangeAsync(user.Id, user.Id, "not-an-address");

        Assert.AreEqual(AccountEmailChangeStatus.Invalid, result.Status);
    }

    [TestMethod]
    public async Task Request_RegularUserForOtherUser_Forbidden()
    {
        var user = await AddUserAsync("user@test.local");
        var other = await AddUserAsync("other@test.local");

        var result = await _service.RequestChangeAsync(user.Id, other.Id, "new@test.local");

        Assert.AreEqual(AccountEmailChangeStatus.Forbidden, result.Status);
    }

    [TestMethod]
    public async Task Request_ManagerForSystemAdmin_Forbidden()
    {
        var manager = await AddUserAsync("manager@test.local", UserClaim.Manager);
        var otherAdmin = await AddUserAsync("second-admin@test.local", UserClaim.SystemAdmin);

        var result = await _service.RequestChangeAsync(manager.Id, otherAdmin.Id, "new@test.local");

        Assert.AreEqual(AccountEmailChangeStatus.Forbidden, result.Status);
    }

    [TestMethod]
    public async Task Request_ManagerForRegularUser_SendsConfirmationToNewAddress()
    {
        var manager = await AddUserAsync("manager@test.local", UserClaim.Manager);
        var user = await AddUserAsync("user@test.local");

        var result = await _service.RequestChangeAsync(manager.Id, user.Id, "new@test.local");

        Assert.AreEqual(AccountEmailChangeStatus.ConfirmationSent, result.Status);
        _emailServiceMock.Verify(e => e.SendAccountEmailChangeConfirmationAsync("new@test.local", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task Request_TooManyRequests_Rejected()
    {
        var user = await AddUserAsync("old@test.local");
        for (var i = 0; i < AccountEmailChangeService.MaxRequestsPerHour; i++)
        {
            Assert.AreEqual(AccountEmailChangeStatus.ConfirmationSent, (await _service.RequestChangeAsync(user.Id, user.Id, $"new{i}@test.local")).Status);
        }

        var result = await _service.RequestChangeAsync(user.Id, user.Id, "one-more@test.local");

        Assert.AreEqual(AccountEmailChangeStatus.Conflict, result.Status);
    }

    [TestMethod]
    public async Task Confirm_UpdatesSenderOfOpenRelayTokens()
    {
        var user = await AddUserAsync("old@test.local");
        _dbContext.EmailRelayTokens.Add(new EmailRelayToken
        {
            Id = Guid.NewGuid(),
            TokenAddress = "relay-abc@relay.test.local",
            SenderEmail = "old@test.local",
            CreatedByUserId = user.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        });
        await _dbContext.SaveChangesAsync();
        await _service.RequestChangeAsync(user.Id, user.Id, "new@test.local");

        await _service.ConfirmChangeAsync(ExtractToken());

        _dbContext.ChangeTracker.Clear();
        Assert.AreEqual("new@test.local", (await _dbContext.EmailRelayTokens.SingleAsync()).SenderEmail);
    }

    private async Task<User> AddUserAsync(string email, params UserClaim[] claims)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            FirstName = "Test",
            LastName = "User",
            Claims = [.. claims]
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();
        return user;
    }

    private Task<User> ReloadUserAsync(Guid id) => _dbContext.Users.AsNoTracking().SingleAsync(u => u.Id == id);

    private string ExtractToken()
    {
        Assert.IsNotNull(_lastConfirmUrl);
        var query = new Uri(_lastConfirmUrl).Query.TrimStart('?');
        var token = query.Split('&').Single(p => p.StartsWith("token=", StringComparison.Ordinal))["token=".Length..];
        return Uri.UnescapeDataString(token);
    }
}
