using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Igevo.Vernetzt.Tests.Security;

[TestClass]
public sealed class HttpPipelineSecurityTests
{
    [TestMethod]
    public async Task Responses_ContainRestrictiveBrowserSecurityHeaders()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Test"));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/version");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var csp = string.Join(' ', response.Headers.GetValues("Content-Security-Policy"));
        StringAssert.Contains(csp, "script-src 'self'");
        StringAssert.Contains(csp, "object-src 'none'");
        StringAssert.Contains(csp, "frame-ancestors 'none'");
        Assert.AreEqual("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [TestMethod]
    public async Task UnknownReservedApiPath_IsNotHandledBySpaFallback()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Test"));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/route-that-does-not-exist");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.AreNotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [TestMethod]
    public async Task CookieAuthenticatedUnsafeRequest_RequiresConfiguredOriginInProduction()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("App:BaseUrl", "http://localhost");
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/route-that-does-not-exist");
        request.Headers.Add("Cookie", "vernetzt.session=untrusted-placeholder");

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, await response.Content.ReadAsStringAsync());
    }
}
