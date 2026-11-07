using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Igevo.Vernetzt.Core.DTOs.Public;
using Igevo.Vernetzt.Core.DTOs.Settings;
using ImageMagick;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Igevo.Vernetzt.Tests.Integration;

/// <summary>
/// Portal texts and logo: maintained by system admins, delivered through the public API.
/// </summary>
[TestClass]
public sealed class PortalSettingsIntegrationTests
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
    public async Task UpdatePortal_NonAdmin_IsForbidden()
    {
        using var request = AdminRequest(HttpMethod.Put, "/api/settings/portal", role: "Manager");
        request.Content = JsonContent.Create(new PortalSettingsDto());

        Assert.AreEqual(HttpStatusCode.Forbidden, (await _client.SendAsync(request)).StatusCode);
    }

    [TestMethod]
    public async Task UpdatePortal_SanitizesHtmlAndPublicApiReturnsOnlyActiveMode()
    {
        // Prime the output cache: the update must evict it.
        var before = await GetPublicPortalAsync();
        Assert.IsNull(before.Title);
        Assert.IsNull(before.Imprint.Html);

        var response = await PutPortalAsync(new PortalSettingsDto
        {
            Title = "Vereine in Musterstadt",
            Subtitle = "Alles an einem Ort",
            Imprint = new PortalLegalPageDto
            {
                Mode = "text",
                Html = "<p onclick=\"alert(1)\">Stadt <strong>Musterstadt</strong></p><script>alert(1)</script>",
                Url = "https://example.org/kept-for-switching"
            },
            Privacy = new PortalLegalPageDto { Mode = "link", Html = "<p>versteckt</p>", Url = "https://example.org/datenschutz" },
            ProviderName = "Stadt Musterstadt",
            ProviderUrl = "https://musterstadt.example"
        });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var portal = await GetPublicPortalAsync();
        Assert.AreEqual("Vereine in Musterstadt", portal.Title);
        Assert.AreEqual("Alles an einem Ort", portal.Subtitle);
        Assert.AreEqual("text", portal.Imprint.Mode);
        Assert.AreEqual("<p>Stadt <strong>Musterstadt</strong></p>", portal.Imprint.Html);
        Assert.IsNull(portal.Imprint.Url);
        Assert.AreEqual("link", portal.Privacy.Mode);
        Assert.AreEqual("https://example.org/datenschutz", portal.Privacy.Url);
        Assert.IsNull(portal.Privacy.Html);
        Assert.AreEqual("Stadt Musterstadt", portal.Provider?.Name);
        Assert.IsNull(portal.LogoUrl);
    }

    [TestMethod]
    public async Task Notice_IsPublishedOnlyWithText()
    {
        Assert.IsNull((await GetPublicPortalAsync()).Notice);

        await PutPortalAsync(new PortalSettingsDto { NoticeText = "  Der Datenbestand befindet sich im Aufbau.  ", NoticeLevel = "warning" });
        var notice = (await GetPublicPortalAsync()).Notice;
        Assert.AreEqual("warning", notice?.Level);
        Assert.AreEqual("Der Datenbestand befindet sich im Aufbau.", notice?.Text);

        await PutPortalAsync(new PortalSettingsDto { NoticeText = " ", NoticeLevel = "warning" });
        Assert.IsNull((await GetPublicPortalAsync()).Notice);
    }

    [TestMethod]
    public async Task UpdatePortal_RejectsUnknownNoticeLevel()
    {
        var response = await PutPortalAsync(new PortalSettingsDto { NoticeText = "Hinweis", NoticeLevel = "danger" });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task UpdatePortal_EmptyRichTextIsStoredAsEmpty()
    {
        await PutPortalAsync(new PortalSettingsDto { Imprint = new PortalLegalPageDto { Html = "<p> </p><p><br></p>" } });

        Assert.IsNull((await GetPublicPortalAsync()).Imprint.Html);
    }

    [TestMethod]
    [DataRow("javascript:alert(1)")]
    [DataRow("")]
    public async Task UpdatePortal_LinkModeRequiresHttpUrl(string url)
    {
        var response = await PutPortalAsync(new PortalSettingsDto
        {
            Imprint = new PortalLegalPageDto { Mode = "link", Url = url }
        });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Logo_UploadIsPublishedWithVersionedImmutableUrlAndCanBeRemoved()
    {
        Assert.AreEqual(HttpStatusCode.NotFound, (await _client.GetAsync("/api/public/v1/portal/logo")).StatusCode);

        using var image = new MagickImage(MagickColors.SteelBlue, 900, 300);
        using var upload = AdminRequest(HttpMethod.Post, "/api/settings/portal/logo");
        var file = new ByteArrayContent(image.ToByteArray(MagickFormat.Png));
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        upload.Content = new MultipartFormDataContent { { file, "file", "logo.png" } };
        Assert.AreEqual(HttpStatusCode.OK, (await _client.SendAsync(upload)).StatusCode);

        var logoUrl = (await GetPublicPortalAsync()).LogoUrl;
        Assert.IsNotNull(logoUrl);
        StringAssert.StartsWith(logoUrl, "/api/public/v1/portal/logo?v=");

        var logo = await _client.GetAsync(logoUrl);
        Assert.AreEqual(HttpStatusCode.OK, logo.StatusCode);
        Assert.AreEqual("image/webp", logo.Content.Headers.ContentType?.MediaType);
        Assert.AreEqual(TimeSpan.FromDays(365), logo.Headers.CacheControl?.MaxAge);
        using var stored = new MagickImage(await logo.Content.ReadAsByteArrayAsync());
        Assert.AreEqual(3d, stored.Width / (double)stored.Height, 0.01, "The logo must keep its aspect ratio.");

        using var delete = AdminRequest(HttpMethod.Delete, "/api/settings/portal/logo");
        Assert.AreEqual(HttpStatusCode.OK, (await _client.SendAsync(delete)).StatusCode);
        Assert.IsNull((await GetPublicPortalAsync()).LogoUrl);
        Assert.AreEqual(HttpStatusCode.NotFound, (await _client.GetAsync(logoUrl)).StatusCode);
    }

    private async Task<HttpResponseMessage> PutPortalAsync(PortalSettingsDto dto)
    {
        using var request = AdminRequest(HttpMethod.Put, "/api/settings/portal");
        request.Content = JsonContent.Create(dto);
        return await _client.SendAsync(request);
    }

    private async Task<PublicPortalDto> GetPublicPortalAsync()
    {
        var response = await _client.GetAsync("/api/public/v1/portal");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PublicPortalDto>())!;
    }

    private HttpRequestMessage AdminRequest(HttpMethod method, string url, string role = "SystemAdmin")
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestHeaderAuthenticationHandler.UserIdHeaderName, _adminId.ToString());
        request.Headers.Add(TestHeaderAuthenticationHandler.RolesHeaderName, role);
        return request;
    }
}
