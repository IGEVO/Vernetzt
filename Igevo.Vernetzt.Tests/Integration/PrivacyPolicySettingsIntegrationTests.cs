using Igevo.Vernetzt.Tests.TestInfrastructure;
using System.Net;
using System.Net.Http.Json;
using Igevo.Vernetzt.Core.DTOs;
using Igevo.Vernetzt.Core.DTOs.Settings;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Igevo.Vernetzt.Tests.Integration;

/// <summary>
/// Privacy policy of the app: external link or own text, readable without sign-in at /datenschutz.
/// </summary>
[TestClass]
public sealed class PrivacyPolicySettingsIntegrationTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private readonly Guid _adminId = Guid.NewGuid();

    [TestInitialize]
    public void Setup()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("App:BaseUrl", "https://vernetzt.test");
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
    public async Task OwnText_IsSanitizedAndPublishedWithoutSignIn()
    {
        var response = await PutSettingsAsync(new AppSettingsDto
        {
            ContactName = "Kontakt", ContactEmail = "kontakt@example.org",
            PrivacyPolicyMode = "text", PrivacyPolicyUrl = "https://example.org/alt",
            PrivacyPolicyHtml = "<h2>Datenschutz</h2><p>Text</p><script>alert(1)</script>"
        });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var policy = await GetAsync<PublicPrivacyPolicyDto>("/api/settings/privacy-policy");
        Assert.AreEqual("text", policy.Mode);
        Assert.Contains("<h2>Datenschutz</h2>", policy.Html!);
        Assert.DoesNotContain("script", policy.Html!);
        Assert.IsNull(policy.Url);

        Assert.AreEqual("https://vernetzt.test/datenschutz", await GetAsync<string>("/api/settings/privacy-policy-url"));
    }

    [TestMethod]
    public async Task Link_IsPublishedAsUrl()
    {
        var response = await PutSettingsAsync(new AppSettingsDto
        {
            ContactName = "Kontakt", ContactEmail = "kontakt@example.org",
            PrivacyPolicyMode = "link", PrivacyPolicyUrl = "https://example.org/datenschutz", PrivacyPolicyHtml = "<p>Entwurf</p>"
        });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var policy = await GetAsync<PublicPrivacyPolicyDto>("/api/settings/privacy-policy");
        Assert.AreEqual("link", policy.Mode);
        Assert.AreEqual("https://example.org/datenschutz", policy.Url);
        Assert.IsNull(policy.Html);

        // The draft text is kept for a later switch back to own text
        using var get = AdminRequest(HttpMethod.Get, "/api/settings");
        var settings = (await (await _client.SendAsync(get)).Content.ReadFromJsonAsync<ApiResponse<AppSettingsDto>>())!.Data!;
        Assert.AreEqual("<p>Entwurf</p>", settings.PrivacyPolicyHtml);
    }

    [TestMethod]
    [DataRow("text", "", "")]
    [DataRow("text", "", "<p> </p>")]
    [DataRow("link", "", "<p>Text</p>")]
    [DataRow("link", "javascript:alert(1)", "")]
    public async Task IncompleteOrInvalidPolicy_IsRejected(string mode, string url, string html)
    {
        var response = await PutSettingsAsync(new AppSettingsDto
        {
            ContactName = "Kontakt", ContactEmail = "kontakt@example.org",
            PrivacyPolicyMode = mode, PrivacyPolicyUrl = url, PrivacyPolicyHtml = html
        });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<HttpResponseMessage> PutSettingsAsync(AppSettingsDto dto)
    {
        using var request = AdminRequest(HttpMethod.Put, "/api/settings");
        request.Content = JsonContent.Create(dto);
        return await _client.SendAsync(request);
    }

    private async Task<T> GetAsync<T>(string url)
    {
        var response = await _client.GetAsync(url);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<T>>())!.Data!;
    }

    private HttpRequestMessage AdminRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestHeaderAuthenticationHandler.UserIdHeaderName, _adminId.ToString());
        request.Headers.Add(TestHeaderAuthenticationHandler.RolesHeaderName, "SystemAdmin");
        return request;
    }
}
