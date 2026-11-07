using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Igevo.Vernetzt.Tests.Integration;

/// <summary>
/// Detailed health endpoint against the real application (temporary database, storage and backup directories).
/// </summary>
[TestClass]
public class HealthControllerIntegrationTests
{
    private const string Token = "0123456789abcdef0123456789abcdef"; // gitleaks:allow (test dummy)

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    [TestInitialize]
    public void Setup()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("Health:Token", Token);
        });
        _client = _factory.CreateClient();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client?.Dispose();
        _factory?.Dispose();
    }

    [TestMethod]
    public async Task Details_ReportsDatabaseStorageAndBackupsHealthy_AndLeavesNoTestFile()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/health/details");
        request.Headers.Add("X-Health-Token", Token);

        var response = await _client.SendAsync(request);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var checks = body.GetProperty("checks").EnumerateArray()
            .ToDictionary(c => c.GetProperty("name").GetString()!, c => c.GetProperty("status").GetString());
        Assert.AreEqual("healthy", checks["database"]);
        Assert.AreEqual("healthy", checks["storage"]);
        Assert.AreEqual("healthy", checks["backups"]);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var storage = _factory.Services.GetRequiredService<IFileStorageService>();
        Assert.IsFalse(await storage.FileExistsAsync("avatars/.healthcheck"));
        var avatarDirectory = Path.Combine(GetStorageRoot(), "avatars");
        Assert.AreEqual(0, Directory.Exists(avatarDirectory) ? Directory.GetFiles(avatarDirectory).Length : 0,
            "the storage check must delete its test file");
    }

    private string GetStorageRoot()
    {
        // The Test environment stores uploads next to its temporary database file.
        var probe = _factory.Services.GetRequiredService<IFileStorageService>();
        var field = probe.GetType().GetField("_storageRootPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (string)field!.GetValue(probe)!;
    }
}
