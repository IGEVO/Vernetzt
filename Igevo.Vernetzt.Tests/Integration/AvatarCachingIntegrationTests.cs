using Igevo.Vernetzt.Tests.TestInfrastructure;
using System.Net;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Igevo.Vernetzt.Tests.Integration;

/// <summary>
/// Avatars are revalidated by the browser (ETag) instead of a version marker in local storage.
/// </summary>
[TestClass]
public sealed class AvatarCachingIntegrationTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private readonly Guid _userId = Guid.NewGuid();

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void Setup()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.ConfigureTestServices(services =>
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestHeaderAuthenticationHandler.SchemeName;
                        options.DefaultChallengeScheme = TestHeaderAuthenticationHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestHeaderAuthenticationHandler>(
                        TestHeaderAuthenticationHandler.SchemeName, _ => { }));
        });
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [TestMethod]
    public async Task Avatar_IsPrivateWithETag_AndRevalidatesUntilItChanges()
    {
        await StoreAvatarAsync([1, 2, 3]);

        using var first = await SendAsync(null);
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        Assert.IsTrue(first.Headers.CacheControl?.Private, "Cache-Control must be private");
        Assert.IsTrue(first.Headers.CacheControl?.NoCache, "Cache-Control must require revalidation");
        Assert.IsFalse(first.Headers.CacheControl?.Public ?? false);
        var etag = first.Headers.ETag;
        Assert.IsNotNull(etag);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, await first.Content.ReadAsByteArrayAsync(TestContext.CancellationToken));

        using var unchanged = await SendAsync(etag.Tag);
        Assert.AreEqual(HttpStatusCode.NotModified, unchanged.StatusCode);

        await StoreAvatarAsync([4, 5, 6]);
        using var changed = await SendAsync(etag.Tag);
        Assert.AreEqual(HttpStatusCode.OK, changed.StatusCode);
        Assert.AreNotEqual(etag.Tag, changed.Headers.ETag?.Tag);
    }

    [TestMethod]
    public async Task MissingAvatar_ReturnsNotFound()
    {
        using var response = await SendAsync(null);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task StoreAvatarAsync(byte[] content)
    {
        var storage = _factory.Services.GetRequiredService<IFileStorageService>();
        using var stream = new MemoryStream(content);
        await storage.UploadFileWithExactPathAsync(stream, $"avatars/users/{_userId}/avatar-medium.webp", TestContext.CancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(string? ifNoneMatch)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/avatars/users/{_userId}");
        request.Headers.Add(TestHeaderAuthenticationHandler.UserIdHeaderName, _userId.ToString());
        request.Headers.Add(TestHeaderAuthenticationHandler.RolesHeaderName, "");
        if (ifNoneMatch != null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
        }

        return await _client.SendAsync(request, TestContext.CancellationToken);
    }
}
