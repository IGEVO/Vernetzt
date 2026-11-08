using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Igevo.Vernetzt.Tests.Integration;

/// <summary>
/// The user documentation is hosted on GitHub Pages; legacy /help links of an instance must redirect there.
/// </summary>
[TestClass]
public class HelpRedirectIntegrationTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    [TestInitialize]
    public void Setup()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("environment", "Test"));
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client?.Dispose();
        _factory?.Dispose();
    }

    [TestMethod]
    [DataRow("/help", "https://igevo.github.io/Vernetzt/")]
    [DataRow("/help/", "https://igevo.github.io/Vernetzt/")]
    [DataRow("/help/index.html", "https://igevo.github.io/Vernetzt/")]
    [DataRow("/help/app/amtsadressen/index.html", "https://igevo.github.io/Vernetzt/app/amtsadressen/")]
    [DataRow("/help/app/amtsadressen/", "https://igevo.github.io/Vernetzt/app/amtsadressen/")]
    public async Task Help_RedirectsToPublicUserDocs(string path, string expected)
    {
        var response = await _client.GetAsync(path);

        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        Assert.AreEqual(expected, response.Headers.Location?.ToString());
    }
}
