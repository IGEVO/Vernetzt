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
public class AuditLogsAuthorizationIntegrationTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private Guid _adminUserId;
    private Guid _regularUserId;

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
    [DataRow("/api/audit-logs")]
    [DataRow("/api/audit-logs/summary")]
    [DataRow("/api/audit-logs/entity-types")]
    public async Task AuditEndpoints_WithoutAuthentication_ReturnsUnauthorized(string endpoint)
    {
        var response = await _client.GetAsync(endpoint);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    [DataRow("/api/audit-logs")]
    [DataRow("/api/audit-logs/summary")]
    [DataRow("/api/audit-logs/entity-types")]
    public async Task AuditEndpoints_NonAdminUser_ReturnsForbidden(string endpoint)
    {
        using var request = CreateAuthenticatedRequest(endpoint, _regularUserId);
        var response = await _client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    [DataRow("/api/audit-logs")]
    [DataRow("/api/audit-logs/summary")]
    [DataRow("/api/audit-logs/entity-types")]
    public async Task AuditEndpoints_SystemAdmin_ReturnsOk(string endpoint)
    {
        using var request = CreateAuthenticatedRequest(endpoint, _adminUserId);
        var response = await _client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private HttpRequestMessage CreateAuthenticatedRequest(string endpoint, Guid userId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Add(TestHeaderAuthenticationHandler.UserIdHeaderName, userId.ToString());
        return request;
    }

    private async Task SeedUsersAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VernetztDbContext>();

        _adminUserId = Guid.NewGuid();
        _regularUserId = Guid.NewGuid();

        dbContext.Users.AddRange(
            new User
            {
                Id = _adminUserId,
                FirstName = "Audit",
                LastName = "Admin",
                Email = $"audit-admin-{_adminUserId:N}@test.local",
                EmailVerified = true,
                Claims = [UserClaim.SystemAdmin]
            },
            new User
            {
                Id = _regularUserId,
                FirstName = "Audit",
                LastName = "User",
                Email = $"audit-user-{_regularUserId:N}@test.local",
                EmailVerified = true,
                Claims = []
            });

        await dbContext.SaveChangesAsync();

        // Ensure users are visible to AuthorizationService lookup in request scope.
        dbContext.ChangeTracker.Clear();
        Assert.IsNotNull(await dbContext.Users.FirstOrDefaultAsync(u => u.Id == _adminUserId));
        Assert.IsNotNull(await dbContext.Users.FirstOrDefaultAsync(u => u.Id == _regularUserId));
    }
}
