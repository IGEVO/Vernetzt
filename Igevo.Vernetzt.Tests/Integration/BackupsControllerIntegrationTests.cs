using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Igevo.Vernetzt.Tests.Integration;

/// <summary>
/// Backups API against the real application with its temporary SQLite database.
/// </summary>
[TestClass]
public class BackupsControllerIntegrationTests
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

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SeedUsersAsync();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client?.Dispose();
        _factory?.Dispose();
    }

    [TestMethod]
    public async Task Backups_NonAdminUser_ReturnsForbidden()
    {
        using var get = CreateRequest(HttpMethod.Get, _regularUserId, role: "Manager");
        using var post = CreateRequest(HttpMethod.Post, _regularUserId, role: "Manager");

        Assert.AreEqual(HttpStatusCode.Forbidden, (await _client.SendAsync(get)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await _client.SendAsync(post)).StatusCode);
    }

    [TestMethod]
    public async Task CreateBackup_SystemAdmin_CreatesBackupListedInStatus()
    {
        using var post = CreateRequest(HttpMethod.Post, _adminUserId, role: "SystemAdmin");
        var created = await _client.SendAsync(post);
        Assert.AreEqual(HttpStatusCode.OK, created.StatusCode);

        var createdData = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        var fileName = createdData.GetProperty("fileName").GetString();
        StringAssert.EndsWith(fileName, "-manual.db");

        using var get = CreateRequest(HttpMethod.Get, _adminUserId, role: "SystemAdmin");
        var status = await _client.SendAsync(get);
        Assert.AreEqual(HttpStatusCode.OK, status.StatusCode);

        var data = (await status.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        var backups = data.GetProperty("backups").EnumerateArray().Select(b => b.GetProperty("fileName").GetString()).ToList();
        CollectionAssert.Contains(backups, fileName);
        Assert.IsFalse(data.GetProperty("scheduleEnabled").GetBoolean(), "the Test environment disables the schedule");
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, Guid userId, string role)
    {
        var request = new HttpRequestMessage(method, "/api/backups");
        request.Headers.Add(TestHeaderAuthenticationHandler.UserIdHeaderName, userId.ToString());
        request.Headers.Add(TestHeaderAuthenticationHandler.RolesHeaderName, role);
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
                FirstName = "Backup",
                LastName = "Admin",
                Email = $"backup-admin-{_adminUserId:N}@test.local",
                EmailVerified = true,
                Claims = [UserClaim.SystemAdmin]
            },
            new User
            {
                Id = _regularUserId,
                FirstName = "Backup",
                LastName = "User",
                Email = $"backup-user-{_regularUserId:N}@test.local",
                EmailVerified = true,
                Claims = [UserClaim.Manager]
            });

        await dbContext.SaveChangesAsync();
    }
}
