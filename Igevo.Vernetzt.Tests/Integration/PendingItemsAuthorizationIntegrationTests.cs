using System.Net;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Igevo.Vernetzt.Tests.Integration;

[TestClass]
public class PendingItemsAuthorizationIntegrationTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private Guid _adminUserId;
    private Guid _regularUserId;
    private Guid _managerUserId;

    [TestInitialize]
    public async Task Setup()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Test");
                builder.ConfigureTestServices(services =>
                {
                    services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestHeaderAuthenticationHandler.SchemeName;
                        options.DefaultChallengeScheme = TestHeaderAuthenticationHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestHeaderAuthenticationHandler>(
                        TestHeaderAuthenticationHandler.SchemeName,
                        _ => { });
                });
            });

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        await SeedUsersAsync();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client?.Dispose();
        _factory?.Dispose();
    }

    [TestMethod]
    public async Task GetPendingItems_WithoutAuthentication_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/pending-items");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task GetPendingItems_RegularUser_ReturnsForbidden()
    {
        using var request = CreateAuthenticatedRequest(HttpMethod.Get, "/api/pending-items", _regularUserId);
        var response = await _client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task RevokeInvitation_RegularUser_ReturnsForbidden()
    {
        using var request = CreateAuthenticatedRequest(HttpMethod.Delete, $"/api/pending-items/invitations/{Guid.NewGuid()}", _regularUserId);
        var response = await _client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task GetPendingItems_Manager_ReturnsOk()
    {
        using var request = CreateAuthenticatedRequest(HttpMethod.Get, "/api/pending-items", _managerUserId, "Manager");
        var response = await _client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task GetPendingItems_SystemAdmin_ReturnsOk()
    {
        using var request = CreateAuthenticatedRequest(HttpMethod.Get, "/api/pending-items", _adminUserId, "SystemAdmin");
        var response = await _client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task RevokeInvitation_UnknownInvitation_ReturnsNotFound()
    {
        using var request = CreateAuthenticatedRequest(HttpMethod.Delete, $"/api/pending-items/invitations/{Guid.NewGuid()}", _managerUserId, "Manager");
        var response = await _client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string endpoint, Guid userId, string? role = null)
    {
        var request = new HttpRequestMessage(method, endpoint);
        request.Headers.Add(TestHeaderAuthenticationHandler.UserIdHeaderName, userId.ToString());
        if (role != null) request.Headers.Add(TestHeaderAuthenticationHandler.RolesHeaderName, role);
        return request;
    }

    private async Task SeedUsersAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VernetztDbContext>();

        _adminUserId = Guid.NewGuid();
        _managerUserId = Guid.NewGuid();
        _regularUserId = Guid.NewGuid();

        dbContext.Users.AddRange(
            new User { Id = _adminUserId, FirstName = "Pending", LastName = "Admin", Email = $"pending-admin-{_adminUserId:N}@test.local", EmailVerified = true, Claims = [UserClaim.SystemAdmin] },
            new User { Id = _managerUserId, FirstName = "Pending", LastName = "Manager", Email = $"pending-manager-{_managerUserId:N}@test.local", EmailVerified = true, Claims = [UserClaim.Manager] },
            new User { Id = _regularUserId, FirstName = "Pending", LastName = "User", Email = $"pending-user-{_regularUserId:N}@test.local", EmailVerified = true, Claims = [] });

        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
    }
}
