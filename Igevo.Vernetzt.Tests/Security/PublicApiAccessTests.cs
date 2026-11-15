using System.Net;
using Igevo.Vernetzt.Server.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Igevo.Vernetzt.Tests.Security;

[TestClass]
public sealed class PublicApiAccessTests
{
    private const string Key = "test-public-api-key-0123456789abcdef";

    private static WebApplicationFactory<Program> CreateFactory(string? configuredKey = Key) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(
                new Dictionary<string, string?> { [PublicApiAccessMiddleware.ConfigurationKey] = configuredKey }));
        });

    private static async Task<HttpStatusCode> GetAsync(WebApplicationFactory<Program> factory, string path, params string[] keys)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (keys.Length > 0) request.Headers.Add(PublicApiAccessMiddleware.HeaderName, keys);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    [TestMethod]
    public async Task PublicApi_WithValidKey_IsServed()
    {
        using var factory = CreateFactory();

        Assert.AreEqual(HttpStatusCode.OK, await GetAsync(factory, "/api/public/v1/club-types", Key));
        Assert.AreEqual(HttpStatusCode.OK, await GetAsync(factory, "/api/public/v1/openapi.json", Key));
    }

    [TestMethod]
    [DataRow("/api/public/v1/club-types")]
    [DataRow("/api/public/v1/openapi.json")]
    [DataRow("/API/Public/v1/club-types")]
    [DataRow("/api/public")]
    public async Task PublicApi_WithoutKey_IsNotFound(string path)
    {
        using var factory = CreateFactory();

        Assert.AreEqual(HttpStatusCode.NotFound, await GetAsync(factory, path));
    }

    [TestMethod]
    public async Task PublicApi_WithWrongOrRepeatedKey_IsNotFound()
    {
        using var factory = CreateFactory();

        Assert.AreEqual(HttpStatusCode.NotFound, await GetAsync(factory, "/api/public/v1/club-types", Key + "x"));
        Assert.AreEqual(HttpStatusCode.NotFound, await GetAsync(factory, "/api/public/v1/club-types", Key.ToUpperInvariant()));
        Assert.AreEqual(HttpStatusCode.NotFound, await GetAsync(factory, "/api/public/v1/club-types", "wrong", Key));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("too-short")]
    [DataRow("CHANGE-THIS-TO-A-SECURE-RANDOM-PUBLIC-API-KEY")]
    public async Task PublicApi_WithoutUsableConfiguredKey_IsClosed(string configuredKey)
    {
        using var factory = CreateFactory(configuredKey);

        Assert.AreEqual(HttpStatusCode.NotFound, await GetAsync(factory, "/api/public/v1/club-types", configuredKey));
    }

    [TestMethod]
    public async Task OtherAnonymousApi_DoesNotNeedKey()
    {
        using var factory = CreateFactory();

        Assert.AreEqual(HttpStatusCode.OK, await GetAsync(factory, "/api/version"));
    }
}
