using Igevo.Vernetzt.Core.DTOs.Auth;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Data;
using Igevo.Vernetzt.Infrastructure.Identity;
using Igevo.Vernetzt.Infrastructure.Observability;
using Igevo.Vernetzt.Infrastructure.Repositories;
using Igevo.Vernetzt.Infrastructure.Services;
using Igevo.Vernetzt.Server.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Igevo.Vernetzt.Tests.TestInfrastructure;
using Microsoft.Data.Sqlite;

namespace Igevo.Vernetzt.Tests.Services;

/// <summary>
/// Tests for the login-link flow. Token creation and validation run through the real
/// ASP.NET Core Identity UserManager and data-protection token provider (in-memory SQLite store).
/// </summary>
[TestClass]
public class AuthServiceTests
{
    private SqliteConnection? _connection;
    private ServiceProvider _provider = null!;
    private IServiceScope _scope = null!;
    private VernetztDbContext _dbContext = null!;
    private Mock<IEmailService> _emailServiceMock = null!;
    private Mock<IAuditLogRepository> _auditLogRepositoryMock = null!;

    [TestInitialize]
    public void Setup() => BuildProvider(TimeSpan.FromMinutes(15));

    [TestCleanup]
    public void Cleanup()
    {
        _scope?.Dispose();
        _provider?.Dispose();
        _connection?.Dispose();
    }

    [TestMethod]
    public async Task CreateLoginLinkToken_ContextEmailHint_CooldownPreventsDuplicateSend()
    {
        await SeedVerifiedContextEmailAsync("vorstand@verein-a.de");
        var service = CreateService(Config(("Auth:ContextEmailLoginHint:Enabled", "true"), ("Auth:ContextEmailLoginHintCooldownMinutes", "60")));

        var first = await service.CreateLoginLinkTokenAsync("Vorstand@Verein-A.de", "127.0.0.1");
        var second = await service.CreateLoginLinkTokenAsync("vorstand@verein-a.de", "127.0.0.1");

        Assert.IsNull(first);
        Assert.IsNull(second);
        _emailServiceMock.Verify(e => e.SendContextEmailLoginHintAsync("Vorstand@Verein-A.de", It.IsAny<CancellationToken>()), Times.Once);
        Assert.AreEqual(1, await _dbContext.LoginEmailHintRateLimits.CountAsync());
    }

    [TestMethod]
    public async Task CreateLoginLinkToken_ContextEmailHintDisabled_DoesNotSendHint()
    {
        await SeedVerifiedContextEmailAsync("vorstand@verein-b.de");
        var service = CreateService(Config(("Auth:ContextEmailLoginHint:Enabled", "false")));

        var result = await service.CreateLoginLinkTokenAsync("vorstand@verein-b.de", "127.0.0.1");

        Assert.IsNull(result);
        _emailServiceMock.Verify(e => e.SendContextEmailLoginHintAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.AreEqual(0, await _dbContext.LoginEmailHintRateLimits.CountAsync());
    }

    [TestMethod]
    public async Task CreateLoginLinkToken_UnknownEmail_ReturnsNullAndLogsFailedAttempt()
    {
        var service = CreateService(Config());

        var result = await service.CreateLoginLinkTokenAsync("niemand@example.com", "127.0.0.1");

        Assert.IsNull(result);
        VerifyAudit(AuditAction.LoginAttemptFailed, Times.Once());
    }

    [TestMethod]
    public async Task LoginLink_ValidToken_CanBeRedeemedExactlyOnce()
    {
        var user = await SeedUserAsync("anna@example.com");
        var service = CreateService(Config());

        var link = await service.CreateLoginLinkTokenAsync("anna@example.com", "127.0.0.1");

        Assert.IsNotNull(link);
        Assert.AreEqual(user.Id, link.UserId);
        Assert.IsTrue(await service.RedeemLoginLinkTokenAsync(user.Id, link.Token, "127.0.0.1"));
        Assert.IsFalse(await service.RedeemLoginLinkTokenAsync(user.Id, link.Token, "127.0.0.1"), "A login link must only work once.");
        Assert.AreEqual(1, await _dbContext.ConsumedLoginTokens.CountAsync());
    }

    [TestMethod]
    public async Task LoginLink_WhitespaceVariantsOfUsedToken_AreRejected()
    {
        var user = await SeedUserAsync("anna@example.com");
        var service = CreateService(Config());

        var link = await service.CreateLoginLinkTokenAsync("anna@example.com", "127.0.0.1");

        Assert.IsNotNull(link);
        Assert.IsTrue(await service.RedeemLoginLinkTokenAsync(user.Id, link.Token, null));
        Assert.IsFalse(await service.RedeemLoginLinkTokenAsync(user.Id, " " + link.Token, null));
        Assert.IsFalse(await service.RedeemLoginLinkTokenAsync(user.Id, link.Token + "\r\n", null));
        Assert.IsFalse(await service.RedeemLoginLinkTokenAsync(user.Id, link.Token.Insert(10, "\t"), null));
    }

    [TestMethod]
    public async Task CreateLoginLinkToken_PerUserBudget_StopsIssuingLinks()
    {
        await SeedUserAsync("anna@example.com");
        var service = CreateService(Config(("Auth:LoginLinkMaxPerUserPerHour", "2")));

        Assert.IsNotNull(await service.CreateLoginLinkTokenAsync("anna@example.com", "10.0.0.1"));
        Assert.IsNotNull(await service.CreateLoginLinkTokenAsync("anna@example.com", "10.0.0.2"));
        Assert.IsNull(await service.CreateLoginLinkTokenAsync("anna@example.com", "10.0.0.3"));
    }

    [TestMethod]
    public async Task LoginLink_Metrics_CountIssuedAndRejectedByReason()
    {
        using var recorder = new MetricsRecorder();
        var user = await SeedUserAsync("anna@example.com");
        var service = CreateService(Config(("Auth:LoginLinkMaxPerUserPerHour", "1")), recorder.Metrics);

        Assert.IsNull(await service.CreateLoginLinkTokenAsync("niemand@example.com", null));
        var link = await service.CreateLoginLinkTokenAsync("anna@example.com", null);
        Assert.IsNotNull(link);
        Assert.IsNull(await service.CreateLoginLinkTokenAsync("anna@example.com", null), "budget of one link is used up");
        Assert.IsTrue(await service.RedeemLoginLinkTokenAsync(user.Id, link.Token, null));
        Assert.IsFalse(await service.RedeemLoginLinkTokenAsync(user.Id, link.Token, null));
        Assert.IsFalse(await service.RedeemLoginLinkTokenAsync(user.Id, "kein-gueltiger-token", null));

        const string rejected = "vernetzt.auth.login_link.rejected";
        Assert.AreEqual(1, recorder.Sum("vernetzt.auth.login_link.issued"));
        Assert.AreEqual(1, recorder.Sum(rejected, ("reason", "unknown_email")));
        Assert.AreEqual(1, recorder.Sum(rejected, ("reason", "budget_exhausted")));
        Assert.AreEqual(1, recorder.Sum(rejected, ("reason", "already_used")));
        Assert.AreEqual(1, recorder.Sum(rejected, ("reason", "invalid_or_expired")));
    }

    [TestMethod]
    public async Task CreateLoginLinkToken_EmailInOtherCase_ResolvesSameUser()
    {
        var user = await SeedUserAsync("anna@example.com");
        var service = CreateService(Config());

        var link = await service.CreateLoginLinkTokenAsync("Anna@Example.COM", "127.0.0.1");

        Assert.IsNotNull(link);
        Assert.AreEqual(user.Id, link.UserId);
    }

    [TestMethod]
    public async Task LoginLink_TokenForOtherUser_IsRejected()
    {
        var anna = await SeedUserAsync("anna@example.com");
        var ben = await SeedUserAsync("ben@example.com");
        var service = CreateService(Config());

        var link = await service.CreateLoginLinkTokenForUserAsync(anna.Id);

        Assert.IsNotNull(link);
        Assert.IsFalse(await service.RedeemLoginLinkTokenAsync(ben.Id, link.Token, null));
    }

    [TestMethod]
    public async Task LoginLink_TamperedToken_IsRejected()
    {
        var user = await SeedUserAsync("anna@example.com");
        var service = CreateService(Config());

        Assert.IsFalse(await service.RedeemLoginLinkTokenAsync(user.Id, "not-a-valid-token", null));
        VerifyAudit(AuditAction.LoginAttemptFailed, Times.Once());
    }

    [TestMethod]
    public async Task LoginLink_ExpiredToken_IsRejected()
    {
        BuildProvider(TimeSpan.FromSeconds(-1));
        var user = await SeedUserAsync("anna@example.com");
        var service = CreateService(Config());

        var link = await service.CreateLoginLinkTokenForUserAsync(user.Id);

        Assert.IsNotNull(link);
        Assert.IsFalse(await service.RedeemLoginLinkTokenAsync(user.Id, link.Token, null));
    }

    [TestMethod]
    public async Task LoginLink_SecurityStampRotation_InvalidatesOpenLinks()
    {
        var user = await SeedUserAsync("anna@example.com");
        var service = CreateService(Config());
        var link = await service.CreateLoginLinkTokenForUserAsync(user.Id);
        Assert.IsNotNull(link);

        // "Log out on all devices" rotates the stamp.
        var userManager = _scope.ServiceProvider.GetRequiredService<UserManager<AppIdentityUser>>();
        var account = await userManager.FindByIdAsync(user.Id.ToString());
        await userManager.UpdateSecurityStampAsync(account!);

        Assert.IsFalse(await service.RedeemLoginLinkTokenAsync(user.Id, link.Token, null));
    }

    [TestMethod]
    public async Task ResetPasskeys_BySystemAdmin_RemovesAllPasskeysAndEndsSessions()
    {
        var admin = await SeedUserAsync("admin@example.com", UserClaim.SystemAdmin);
        var user = await SeedUserAsync("anna@example.com");
        var userManager = _scope.ServiceProvider.GetRequiredService<UserManager<AppIdentityUser>>();
        var account = (await userManager.FindByIdAsync(user.Id.ToString()))!;
        await userManager.AddOrUpdatePasskeyAsync(account, FakePasskey());
        await userManager.AddOrUpdatePasskeyAsync(account, FakePasskey());
        var stampBefore = account.SecurityStamp;

        var result = await CreateService(Config()).ResetPasskeysAsync(admin.Id, user.Id);

        Assert.AreEqual(PasskeyResetStatus.Done, result.Status);
        Assert.AreEqual(2, result.RemovedPasskeys);
        account = (await userManager.FindByIdAsync(user.Id.ToString()))!;
        Assert.IsEmpty(await userManager.GetPasskeysAsync(account));
        Assert.AreNotEqual(stampBefore, account.SecurityStamp);
        VerifyAudit(AuditAction.Update, Times.Once());
    }

    [TestMethod]
    public async Task ResetPasskeys_ByManagerOrOwnAccountOrUnknownUser_IsRejected()
    {
        var admin = await SeedUserAsync("admin@example.com", UserClaim.SystemAdmin);
        var manager = await SeedUserAsync("manager@example.com", UserClaim.Manager);
        var user = await SeedUserAsync("anna@example.com");
        var userManager = _scope.ServiceProvider.GetRequiredService<UserManager<AppIdentityUser>>();
        var account = (await userManager.FindByIdAsync(user.Id.ToString()))!;
        await userManager.AddOrUpdatePasskeyAsync(account, FakePasskey());
        var service = CreateService(Config());

        Assert.AreEqual(PasskeyResetStatus.Forbidden, (await service.ResetPasskeysAsync(manager.Id, user.Id)).Status);
        Assert.AreEqual(PasskeyResetStatus.Forbidden, (await service.ResetPasskeysAsync(user.Id, admin.Id)).Status);
        Assert.AreEqual(PasskeyResetStatus.OwnAccount, (await service.ResetPasskeysAsync(admin.Id, admin.Id)).Status);
        Assert.AreEqual(PasskeyResetStatus.NotFound, (await service.ResetPasskeysAsync(admin.Id, Guid.NewGuid())).Status);

        Assert.HasCount(1, await userManager.GetPasskeysAsync(account));
        VerifyAudit(AuditAction.Update, Times.Never());
    }

    [TestMethod]
    public async Task RecordLogin_UpdatesLastLoginAndWritesAudit()
    {
        var user = await SeedUserAsync("anna@example.com");
        var service = CreateService(Config());

        await service.RecordLoginAsync(user.Id, "LoginLink", "127.0.0.1");

        var reloaded = await _dbContext.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.IsNotNull(reloaded.LastLoginAt);
        VerifyAudit(AuditAction.Login, Times.Once());
    }

    [TestMethod]
    public async Task NewDomainUser_GetsIdentityAccount_AndEmailChangesAreMirrored()
    {
        var user = await SeedUserAsync("anna@example.com");

        var account = await _dbContext.IdentityAccounts.AsNoTracking().SingleAsync(a => a.Id == user.Id);
        Assert.AreEqual("anna@example.com", account.Email);
        Assert.AreEqual(user.Id.ToString("N"), account.UserName);

        user.Email = "anna.neu@example.com";
        await _dbContext.SaveChangesAsync();

        account = await _dbContext.IdentityAccounts.AsNoTracking().SingleAsync(a => a.Id == user.Id);
        Assert.AreEqual("anna.neu@example.com", account.Email);
        Assert.AreEqual("ANNA.NEU@EXAMPLE.COM", account.NormalizedEmail);
    }

    [TestMethod]
    public async Task EnsureIdentityAccount_CreatesMissingAccountForLegacyUser()
    {
        var user = await SeedUserAsync("anna@example.com");
        _dbContext.IdentityAccounts.Remove(await _dbContext.IdentityAccounts.SingleAsync(a => a.Id == user.Id));
        await _dbContext.SaveChangesAsync();
        var service = CreateService(Config());

        var link = await service.CreateLoginLinkTokenForUserAsync(user.Id);

        Assert.IsNotNull(link);
        Assert.IsTrue(await _dbContext.IdentityAccounts.AnyAsync(a => a.Id == user.Id));
        Assert.IsTrue(await service.RedeemLoginLinkTokenAsync(user.Id, link.Token, null));
    }

    private void BuildProvider(TimeSpan loginLinkLifespan)
    {
        _scope?.Dispose();
        _provider?.Dispose();
        _connection?.Dispose();

        var connection = SqliteTestDatabase.OpenConnection();
        _connection = connection;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddDbContext<VernetztDbContext>(o => SqliteTestDatabase.ConfigureSqlite(o, connection));
        services.AddScoped<IUserRepository, UserRepository>();
        services
            .AddIdentityCore<AppIdentityUser>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version3)
            .AddEntityFrameworkStores<VernetztDbContext>()
            .AddTokenProvider<LoginLinkTokenProvider>(LoginLinkTokens.ProviderName);
        services.Configure<LoginLinkTokenProviderOptions>(o => o.TokenLifespan = loginLinkLifespan);

        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
        _dbContext = _scope.ServiceProvider.GetRequiredService<VernetztDbContext>();
        // Created through the Identity-configured context so the schema includes the passkey table.
        _dbContext.Database.EnsureCreated();

        _emailServiceMock = new Mock<IEmailService>();
        _emailServiceMock
            .Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _emailServiceMock
            .Setup(e => e.SendContextEmailLoginHintAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _auditLogRepositoryMock = new Mock<IAuditLogRepository>();
    }

    private AuthService CreateService(IConfiguration configuration, VernetztMetrics? metrics = null) => new(
        _scope.ServiceProvider.GetRequiredService<UserManager<AppIdentityUser>>(),
        _scope.ServiceProvider.GetRequiredService<IUserRepository>(),
        _emailServiceMock.Object,
        _auditLogRepositoryMock.Object,
        _dbContext,
        new MemoryCache(new MemoryCacheOptions()),
        configuration,
        new Mock<ILogger<AuthService>>().Object,
        metrics);

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private void VerifyAudit(AuditAction action, Times times) =>
        _auditLogRepositoryMock.Verify(a => a.LogAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), action, It.IsAny<Guid>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            times);

    internal static UserPasskeyInfo FakePasskey() => new(
        credentialId: Guid.NewGuid().ToByteArray(),
        publicKey: [1, 2, 3],
        createdAt: DateTimeOffset.UtcNow,
        signCount: 0,
        transports: ["internal"],
        isUserVerified: true,
        isBackupEligible: true,
        isBackedUp: true,
        attestationObject: [4],
        clientDataJson: [5]);

    private async Task<User> SeedUserAsync(string email, params UserClaim[] roles)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            FirstName = "Test",
            LastName = "User",
            EmailVerified = true,
            Claims = [.. roles]
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();
        return user;
    }

    private async Task SeedVerifiedContextEmailAsync(string contextEmail)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"primary-{Guid.NewGuid():N}@example.com",
            FirstName = "Context",
            LastName = "User",
            Claims = [],
            EmailVisibility = VisibilityLevel.AllUsers,
            PhoneVisibility = VisibilityLevel.AllUsers
        };
        var club = new Club { Id = Guid.NewGuid(), Name = $"Club {Guid.NewGuid():N}" };
        var assignment = new ContactAssignment
        {
            ConfirmedAt = DateTime.UtcNow,
            Id = Guid.NewGuid(),
            UserId = user.Id,
            ClubId = club.Id,
            ContextEmail = contextEmail,
            ContextEmailNormalized = EmailAddressHelper.Normalize(contextEmail),
            ContextEmailVerified = true,
            Visibility = VisibilityLevel.AllUsers
        };

        await _dbContext.Users.AddAsync(user);
        await _dbContext.Clubs.AddAsync(club);
        await _dbContext.ContactAssignments.AddAsync(assignment);
        await _dbContext.SaveChangesAsync();
    }
}
