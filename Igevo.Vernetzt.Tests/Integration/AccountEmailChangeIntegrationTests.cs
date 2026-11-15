using System.Net;
using System.Net.Http.Json;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace Igevo.Vernetzt.Tests.Integration;

[TestClass]
public class AccountEmailChangeIntegrationTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private Mock<IEmailService> _emailServiceMock = null!;
    private string? _lastConfirmUrl;
    private Guid _userId;
    private Guid _otherUserId;
    private string _suffix = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _suffix = Guid.NewGuid().ToString("N")[..12];
        _emailServiceMock = new Mock<IEmailService>();
        _emailServiceMock
            .Setup(e => e.SendAccountEmailChangeConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string _, string _, string url, CancellationToken _) => _lastConfirmUrl = url)
            .ReturnsAsync(true);
        _emailServiceMock
            .Setup(e => e.SendAccountEmailChangedNoticeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

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
                    services.RemoveAll<IEmailService>();
                    services.AddScoped(_ => _emailServiceMock.Object);
                });
            });

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VernetztDbContext>();
        _userId = Guid.NewGuid();
        _otherUserId = Guid.NewGuid();
        dbContext.Users.AddRange(
            new User { Id = _userId, FirstName = "Change", LastName = "Me", Email = $"change-{_suffix}@test.local", Claims = [] },
            new User { Id = _otherUserId, FirstName = "Other", LastName = "User", Email = $"other-{_suffix}@test.local", Claims = [] });
        await dbContext.SaveChangesAsync();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client?.Dispose();
        _factory?.Dispose();
    }

    [TestMethod]
    public async Task RequestMyEmailChange_WithoutAuthentication_ReturnsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync("/api/users/me/email-change", new { newEmail = $"new-{_suffix}@test.local" });
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task RequestUserEmailChange_RegularUser_ReturnsForbidden()
    {
        using var request = Authenticated(HttpMethod.Post, $"/api/users/{_otherUserId}/email-change", _userId);
        request.Content = JsonContent.Create(new { newEmail = $"new-{_suffix}@test.local" });

        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task RequestAndConfirm_OwnAccount_ChangesLoginAddress()
    {
        var newEmail = $"new-{_suffix}@test.local";
        using var request = Authenticated(HttpMethod.Post, "/api/users/me/email-change", _userId);
        request.Content = JsonContent.Create(new { newEmail });

        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.IsNotNull(_lastConfirmUrl);

        var confirm = await _client.GetAsync(new Uri(_lastConfirmUrl).PathAndQuery);

        Assert.AreEqual(HttpStatusCode.OK, confirm.StatusCode);
        StringAssert.StartsWith(confirm.Content.Headers.ContentType?.MediaType, "text/html");
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VernetztDbContext>();
        Assert.AreEqual(newEmail, (await dbContext.Users.AsNoTracking().SingleAsync(u => u.Id == _userId)).Email);
    }

    [TestMethod]
    public async Task RequestMyEmailChange_AddressTaken_ReturnsConflict()
    {
        using var request = Authenticated(HttpMethod.Post, "/api/users/me/email-change", _userId);
        request.Content = JsonContent.Create(new { newEmail = $"other-{_suffix}@test.local" });

        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
    }

    [TestMethod]
    public async Task Confirm_UnknownToken_ReturnsBadRequestPage()
    {
        var response = await _client.GetAsync("/api/account-email-change/confirm?token=does-not-exist");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        StringAssert.StartsWith(response.Content.Headers.ContentType?.MediaType, "text/html");
    }

    private static HttpRequestMessage Authenticated(HttpMethod method, string endpoint, Guid userId, string? role = null)
    {
        var request = new HttpRequestMessage(method, endpoint);
        request.Headers.Add(TestHeaderAuthenticationHandler.UserIdHeaderName, userId.ToString());
        if (role != null) request.Headers.Add(TestHeaderAuthenticationHandler.RolesHeaderName, role);
        return request;
    }
}
