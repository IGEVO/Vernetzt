using System.Security.Claims;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Infrastructure.Data;
using Igevo.Vernetzt.Infrastructure.Identity;
using Igevo.Vernetzt.Server.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Igevo.Vernetzt.Tests.TestInfrastructure;
using Microsoft.Data.Sqlite;

namespace Igevo.Vernetzt.Tests.Services;

/// <summary>
/// Tests for the passkey requirement of privileged roles.
/// </summary>
[TestClass]
public class PasskeyPolicyTests
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _provider = null!;
    private IServiceScope _scope = null!;
    private VernetztDbContext _dbContext = null!;

    [TestInitialize]
    public void Setup()
    {
        _connection = SqliteTestDatabase.OpenConnection();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<VernetztDbContext>(o => SqliteTestDatabase.ConfigureSqlite(o, _connection));
        // Like Program.cs: schema version 3 includes the passkey table.
        services.AddIdentityCore<AppIdentityUser>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version3)
            .AddEntityFrameworkStores<VernetztDbContext>();
        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
        _dbContext = _scope.ServiceProvider.GetRequiredService<VernetztDbContext>();
        // Created through the Identity-configured context so the schema includes the passkey table.
        _dbContext.Database.EnsureCreated();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _scope.Dispose();
        _provider.Dispose();
        _connection.Dispose();
    }

    [TestMethod]
    public async Task SystemAdmin_WithLoginLinkSession_IsRestricted()
    {
        var admin = await SeedUserAsync(UserClaim.SystemAdmin);

        var state = await CreatePolicy().EvaluateAsync(Principal(admin.Id, PasskeyPolicy.LoginLinkAuthenticationMethod));

        Assert.IsTrue(state.PasskeyRequired);
        Assert.IsTrue(state.IsRestricted);
        Assert.IsFalse(state.HasPasskey);
        Assert.IsTrue(state.CanRegisterPasskey, "The first passkey must be registrable from a login-link session.");
    }

    [TestMethod]
    public async Task SystemAdmin_WithExistingPasskey_CannotRegisterAnotherFromLoginLinkSession()
    {
        var admin = await SeedUserAsync(UserClaim.SystemAdmin);
        _dbContext.Set<IdentityUserPasskey<Guid>>().Add(new IdentityUserPasskey<Guid>
        {
            UserId = admin.Id,
            CredentialId = [1, 2, 3],
            Data = new IdentityPasskeyData { PublicKey = [1], Name = "Test", CreatedAt = DateTimeOffset.UtcNow, AttestationObject = [1], ClientDataJson = [1], Transports = [] }
        });
        await _dbContext.SaveChangesAsync();

        var state = await CreatePolicy().EvaluateAsync(Principal(admin.Id, PasskeyPolicy.LoginLinkAuthenticationMethod));

        Assert.IsTrue(state.IsRestricted);
        Assert.IsTrue(state.HasPasskey);
        Assert.IsFalse(state.CanRegisterPasskey);
    }

    [TestMethod]
    public async Task SystemAdmin_WithPasskeySession_IsNotRestricted()
    {
        var admin = await SeedUserAsync(UserClaim.SystemAdmin);

        var state = await CreatePolicy().EvaluateAsync(Principal(admin.Id, PasskeyPolicy.PasskeyAuthenticationMethod));

        Assert.IsFalse(state.IsRestricted);
    }

    [TestMethod]
    public async Task Manager_IsRestricted_ByDefault()
    {
        var manager = await SeedUserAsync(UserClaim.Manager);
        var policy = CreatePolicy();

        Assert.IsTrue((await policy.EvaluateAsync(Principal(manager.Id, PasskeyPolicy.LoginLinkAuthenticationMethod))).IsRestricted);
        Assert.IsFalse((await policy.EvaluateAsync(Principal(manager.Id, PasskeyPolicy.PasskeyAuthenticationMethod))).IsRestricted);
    }

    [TestMethod]
    public async Task RegularUserAndDataReader_AreNotRestricted_ByDefault()
    {
        var user = await SeedUserAsync();
        var reader = await SeedUserAsync(UserClaim.DataReader);
        var policy = CreatePolicy();

        Assert.IsFalse((await policy.EvaluateAsync(Principal(user.Id, PasskeyPolicy.LoginLinkAuthenticationMethod))).IsRestricted);
        Assert.IsFalse((await policy.EvaluateAsync(Principal(reader.Id, PasskeyPolicy.LoginLinkAuthenticationMethod))).IsRestricted);
    }

    [TestMethod]
    public async Task ConfiguredRoles_ReplaceTheDefault()
    {
        var admin = await SeedUserAsync(UserClaim.SystemAdmin);
        var manager = await SeedUserAsync(UserClaim.Manager);
        var managerOnly = CreatePolicy(new PasskeyPolicyOptions { RequiredForRoles = [UserClaim.Manager] });
        var disabled = CreatePolicy(new PasskeyPolicyOptions { RequiredForRoles = [] });

        Assert.IsTrue((await managerOnly.EvaluateAsync(Principal(manager.Id, PasskeyPolicy.LoginLinkAuthenticationMethod))).IsRestricted);
        Assert.IsFalse((await managerOnly.EvaluateAsync(Principal(admin.Id, PasskeyPolicy.LoginLinkAuthenticationMethod))).IsRestricted);
        Assert.IsFalse((await disabled.EvaluateAsync(Principal(admin.Id, PasskeyPolicy.LoginLinkAuthenticationMethod))).IsRestricted);
    }

    [TestMethod]
    public async Task KeepAuthenticationMethod_CarriesPasskeyClaimIntoRefreshedPrincipal()
    {
        var userId = Guid.NewGuid();
        var context = new SecurityStampRefreshingPrincipalContext
        {
            CurrentPrincipal = Principal(userId, PasskeyPolicy.PasskeyAuthenticationMethod),
            // Like SecurityStampValidator: a fresh principal from the claims factory without the method.
            NewPrincipal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Identity.Application"))
        };

        await PasskeyPolicy.KeepAuthenticationMethod(context);

        Assert.IsTrue(PasskeyPolicy.IsPasskeySession(context.NewPrincipal));
    }

    private PasskeyPolicy CreatePolicy(PasskeyPolicyOptions? options = null)
    {
        var monitor = new Mock<IOptionsMonitor<PasskeyPolicyOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(options ?? new PasskeyPolicyOptions());
        return new PasskeyPolicy(_dbContext, monitor.Object);
    }

    private static ClaimsPrincipal Principal(Guid userId, string authenticationMethod) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.AuthenticationMethod, authenticationMethod)
        ], "Identity.Application"));

    private async Task<User> SeedUserAsync(params UserClaim[] roles)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"{Guid.NewGuid():N}@example.com",
            FirstName = "Test",
            LastName = "User",
            EmailVerified = true,
            Claims = [.. roles]
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();
        return user;
    }
}
