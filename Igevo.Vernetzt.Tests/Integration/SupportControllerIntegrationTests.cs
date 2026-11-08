using System.Net;
using System.Net.Http.Json;
using Igevo.Vernetzt.Core.DTOs;
using Igevo.Vernetzt.Core.DTOs.Settings;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Igevo.Vernetzt.Tests.Integration;

/// <summary>
/// The help menu shows every signed-in user the contact person from the system settings.
/// </summary>
[TestClass]
public sealed class SupportControllerIntegrationTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    [TestInitialize]
    public void Setup()
    {
        var settings = new Mock<IAppSettingsService>();
        settings.Setup(s => s.GetValuesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["ContactName"] = "Ada Lovelace", ["ContactEmail"] = "ada@example.test" });

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => settings.Object);
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestHeaderAuthenticationHandler.SchemeName;
                        options.DefaultChallengeScheme = TestHeaderAuthenticationHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestHeaderAuthenticationHandler>(
                        TestHeaderAuthenticationHandler.SchemeName, _ => { });
            });
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
    public async Task GetContact_RegularUser_ReturnsConfiguredContact()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/support/contact");
        request.Headers.Add(TestHeaderAuthenticationHandler.UserIdHeaderName, Guid.NewGuid().ToString());
        request.Headers.Add(TestHeaderAuthenticationHandler.RolesHeaderName, "User");

        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<SupportContactDto>>();
        Assert.AreEqual("Ada Lovelace", body!.Data!.Name);
        Assert.AreEqual("ada@example.test", body.Data.Email);
    }

    [TestMethod]
    public async Task GetContact_Anonymous_IsUnauthorized()
    {
        var response = await _client.GetAsync("/api/support/contact");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
