using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Igevo.Vernetzt.Tests.Integration;

/// <summary>
/// The container health check (Dockerfile HEALTHCHECK) calls http://localhost:8080/health. A restrictive
/// AllowedHosts (only the public domain) must not reject it, otherwise the container is reported unhealthy.
/// </summary>
[TestClass]
public class HealthHostFilteringIntegrationTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    [TestInitialize]
    public void Setup()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("AllowedHosts", "vernetzt.example.org");
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
    [DataRow("localhost:8080")]
    [DataRow("127.0.0.1:8080")]
    public async Task Health_WithLoopbackHost_IsAllowedDespiteRestrictiveAllowedHosts(string host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Host = host;

        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task Health_WithConfiguredHost_IsAllowed()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Host = "vernetzt.example.org";

        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task Health_WithForeignHost_IsRejected()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Host = "evil.example.com";

        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
