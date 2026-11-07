using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Igevo.Vernetzt.Core.DTOs;
using Igevo.Vernetzt.Core.DTOs.Auth;
using Igevo.Vernetzt.Core.DTOs.Invitations;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Data;
using Igevo.Vernetzt.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace Igevo.Vernetzt.Tests.Integration;

/// <summary>
/// End-to-end tests of the Identity-based auth endpoints over the full HTTP pipeline
/// (temporary SQLite database, captured login-link emails).
/// </summary>
[TestClass]
public class AuthControllerIntegrationTests
{
    private const string AdminEmail = "admin@test.local"; // App:SystemAdminEmail in appsettings.Test.json
    private static readonly Uri BaseAddress = new("https://localhost");

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private readonly List<(string Email, string Url)> _sentLoginLinks = [];

    [TestInitialize]
    public void Setup()
    {
        _sentLoginLinks.Clear();
        var emailServiceMock = new Mock<IEmailService>();
        emailServiceMock
            .Setup(e => e.SendMagicLinkEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, int, CancellationToken>((email, url, _, _) => _sentLoginLinks.Add((email, url)))
            .ReturnsAsync(true);

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Test");
                // Validate the session on every request so that "log out everywhere" is observable immediately.
                builder.UseSetting("Auth:Session:ValidationIntervalMinutes", "0");
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IEmailService>();
                    services.AddSingleton(emailServiceMock.Object);
                });
            });

        _client = CreateClient();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client?.Dispose();
        _factory?.Dispose();
    }

    [TestMethod]
    public async Task RequestLoginLink_KnownEmail_ReturnsOkAndSendsLink()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login-link/request", new LoginLinkRequestDto { Email = AdminEmail });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.HasCount(1, _sentLoginLinks);
        StringAssert.Contains(_sentLoginLinks[0].Url, "/login?uid=");
    }

    [TestMethod]
    public async Task RequestLoginLink_UnknownEmail_ReturnsSameAnswerWithoutSending()
    {
        var known = await _client.PostAsJsonAsync("/api/auth/login-link/request", new LoginLinkRequestDto { Email = AdminEmail });
        var unknown = await _client.PostAsJsonAsync("/api/auth/login-link/request", new LoginLinkRequestDto { Email = "nonexistent@test.com" });

        Assert.AreEqual(HttpStatusCode.OK, unknown.StatusCode);
        var knownBody = await known.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>();
        var unknownBody = await unknown.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>();
        Assert.AreEqual(knownBody!.Data.GetRawText(), unknownBody!.Data.GetRawText());
        Assert.HasCount(1, _sentLoginLinks);
    }

    [TestMethod]
    public async Task RequestLoginLink_InvalidEmail_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login-link/request", new LoginLinkRequestDto { Email = "not-an-email" });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task RequestLoginLink_UnsafeReturnUrl_IsDropped()
    {
        await _client.PostAsJsonAsync("/api/auth/login-link/request", new LoginLinkRequestDto { Email = AdminEmail, ReturnUrl = "//evil.example" });

        Assert.HasCount(1, _sentLoginLinks);
        Assert.DoesNotContain("returnUrl", _sentLoginLinks[0].Url);
    }

    [TestMethod]
    public async Task VerifyLoginLink_InvalidToken_ReturnsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login-link/verify", new VerifyLoginLinkDto { UserId = Guid.NewGuid(), Token = "invalid" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ProtectedEndpoints_WithoutSession_ReturnUnauthorized()
    {
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await _client.PostAsync("/api/auth/logout-all", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/auth/passkeys")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/users/me")).StatusCode);
    }

    [TestMethod]
    public async Task Status_WithoutSession_ReportsNotAuthenticated()
    {
        var status = await ReadStatusAsync(_client);

        Assert.IsFalse(status.GetProperty("isAuthenticated").GetBoolean());
    }

    [TestMethod]
    public async Task LoginLinkFlow_EndToEnd_SignsInOnceAndLogoutAllEndsSessions()
    {
        // 1. Request link and redeem it.
        var (userId, token) = await RequestAndCaptureLinkAsync();
        var verify = await _client.PostAsJsonAsync("/api/auth/login-link/verify", new VerifyLoginLinkDto { UserId = userId, Token = token });
        Assert.AreEqual(HttpStatusCode.OK, verify.StatusCode);
        Assert.IsTrue(verify.Headers.TryGetValues("Set-Cookie", out var cookies));
        var sessionCookie = cookies.Single(c => c.StartsWith("vernetzt.session=", StringComparison.Ordinal));
        StringAssert.Contains(sessionCookie.ToLowerInvariant(), "httponly");
        StringAssert.Contains(sessionCookie.ToLowerInvariant(), "samesite=strict");

        // 2. Session works, roles come from the domain user.
        var status = await ReadStatusAsync(_client);
        Assert.IsTrue(status.GetProperty("isAuthenticated").GetBoolean());
        Assert.AreEqual(userId.ToString(), status.GetProperty("userId").GetString());
        var me = await _client.GetAsync("/api/users/me");
        Assert.AreEqual(HttpStatusCode.OK, me.StatusCode);
        StringAssert.Contains(await me.Content.ReadAsStringAsync(), AdminEmail);

        // 3. The same link cannot be used a second time (e.g. from another browser).
        using var secondBrowser = CreateClient();
        var replay = await secondBrowser.PostAsJsonAsync("/api/auth/login-link/verify", new VerifyLoginLinkDto { UserId = userId, Token = token });
        Assert.AreEqual(HttpStatusCode.Unauthorized, replay.StatusCode);

        // 4. Second device signs in with a fresh link.
        var (_, secondToken) = await RequestAndCaptureLinkAsync();
        var secondVerify = await secondBrowser.PostAsJsonAsync("/api/auth/login-link/verify", new VerifyLoginLinkDto { UserId = userId, Token = secondToken });
        Assert.AreEqual(HttpStatusCode.OK, secondVerify.StatusCode);

        // 5. "Log out everywhere" rotates the security stamp; the current device is signed out immediately.
        using var logoutAll = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout-all");
        logoutAll.Headers.Add("Origin", ConfiguredOrigin());
        var logoutAllResponse = await _client.SendAsync(logoutAll);
        Assert.AreEqual(HttpStatusCode.OK, logoutAllResponse.StatusCode, await logoutAllResponse.Content.ReadAsStringAsync());
        Assert.IsFalse((await ReadStatusAsync(_client)).GetProperty("isAuthenticated").GetBoolean());

        // 6. The other device still holds its cookie, but the rotated security stamp invalidates it.
        Assert.IsFalse((await ReadStatusAsync(secondBrowser)).GetProperty("isAuthenticated").GetBoolean());
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await secondBrowser.GetAsync("/api/users/me")).StatusCode);
    }

    [TestMethod]
    public async Task SystemAdmin_ViaLoginLink_IsLimitedToPasskeySetupUntilPasskeySignIn()
    {
        // The test admin (App:SystemAdminEmail) holds SystemAdmin and has no passkey yet.
        var (userId, token) = await RequestAndCaptureLinkAsync();
        Assert.AreEqual(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/auth/login-link/verify", new VerifyLoginLinkDto { UserId = userId, Token = token })).StatusCode);

        var status = await ReadStatusAsync(_client);
        Assert.IsTrue(status.GetProperty("isAuthenticated").GetBoolean());
        Assert.IsTrue(status.GetProperty("passkeyRequired").GetBoolean());
        Assert.IsFalse(status.GetProperty("hasPasskey").GetBoolean());

        // Own profile and passkey setup work ...
        Assert.AreEqual(HttpStatusCode.OK, (await _client.GetAsync("/api/users/me")).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await _client.GetAsync("/api/auth/passkeys")).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await SendAsync(_client, HttpMethod.Post, "/api/auth/passkeys/creation-options")).StatusCode);

        // ... everything else is blocked, including member features and the passkey reset of other users.
        await AssertPasskeyRequiredAsync(await _client.GetAsync("/api/users"));
        await AssertPasskeyRequiredAsync(await _client.GetAsync("/api/clubs"));
        await AssertPasskeyRequiredAsync(await SendAsync(_client, HttpMethod.Delete, $"/api/users/{Guid.NewGuid()}/passkeys"));
        await AssertPasskeyRequiredAsync(await SendAsync(_client, HttpMethod.Delete, "/api/auth/passkeys/AAAA"));

        // The restriction survives the principal refresh of the security stamp validator (interval 0 in these tests).
        Assert.IsTrue((await ReadStatusAsync(_client)).GetProperty("passkeyRequired").GetBoolean());
    }

    [TestMethod]
    public async Task SystemAdmin_WithPasskey_ViaLoginLink_CannotAddAnotherPasskey()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppIdentityUser>>();
            var admin = await userManager.FindByEmailAsync(AdminEmail);
            Assert.IsNotNull(admin);
            await userManager.AddOrUpdatePasskeyAsync(admin, Services.AuthServiceTests.FakePasskey());
        }

        var (userId, token) = await RequestAndCaptureLinkAsync();
        await _client.PostAsJsonAsync("/api/auth/login-link/verify", new VerifyLoginLinkDto { UserId = userId, Token = token });

        var status = await ReadStatusAsync(_client);
        Assert.IsTrue(status.GetProperty("passkeyRequired").GetBoolean());
        Assert.IsTrue(status.GetProperty("hasPasskey").GetBoolean());

        // Someone with access to the mailbox must not be able to add their own passkey.
        await AssertPasskeyRequiredAsync(await SendAsync(_client, HttpMethod.Post, "/api/auth/passkeys/creation-options"));
        await AssertPasskeyRequiredAsync(await SendAsync(_client, HttpMethod.Post, "/api/auth/passkeys/attestation"));
    }

    [TestMethod]
    public async Task PasskeyPolicy_ConfiguredForOtherRoles_DoesNotRestrictSystemAdmin()
    {
        // The helpers use _factory; restore the original afterwards so Cleanup disposes its started host.
        var originalFactory = _factory;
        using var factory = _factory.WithWebHostBuilder(b => b.UseSetting("Auth:PasskeyPolicy:RequiredForRoles:0", "Manager"));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = BaseAddress, HandleCookies = true, AllowAutoRedirect = false });
        _factory = factory;

        try
        {
            var (userId, token) = await RequestAndCaptureLinkAsync();
            await client.PostAsJsonAsync("/api/auth/login-link/verify", new VerifyLoginLinkDto { UserId = userId, Token = token });

            Assert.IsFalse((await ReadStatusAsync(client)).GetProperty("passkeyRequired").GetBoolean());
            Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync("/api/users")).StatusCode);
        }
        finally
        {
            _factory = originalFactory;
        }
    }

    [TestMethod]
    public async Task UnsafeRequestWithSessionCookie_FromForeignOrigin_IsRejected()
    {
        var (userId, token) = await RequestAndCaptureLinkAsync();
        await _client.PostAsJsonAsync("/api/auth/login-link/verify", new VerifyLoginLinkDto { UserId = userId, Token = token });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout-all");
        request.Headers.Add("Origin", "https://evil.example");
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task AcceptInvitation_DoesNotSignIn_AndSendsFreshLoginLinkToWelcomePage()
    {
        var invitationToken = await SeedInvitationAsync("neu@example.com");

        var response = await _client.PostAsJsonAsync("/api/invitations/accept", new AcceptInvitationDto
        {
            Token = invitationToken,
            FirstName = "Neu",
            LastName = "Mitglied",
            AcceptedPrivacyPolicy = true,
            PreferredLoginMethod = LoginMethod.Passkey
        });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.IsFalse(
            response.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.StartsWith("vernetzt.session=", StringComparison.Ordinal)),
            "Accepting an invitation must not start a session.");
        Assert.IsFalse((await ReadStatusAsync(_client)).GetProperty("isAuthenticated").GetBoolean());

        // A fresh, short-lived login link is sent; it leads to the passkey step of the welcome page.
        Assert.HasCount(1, _sentLoginLinks);
        Assert.AreEqual("neu@example.com", _sentLoginLinks[0].Email);
        var query = QueryHelpers.ParseQuery(new Uri(_sentLoginLinks[0].Url).Query);
        Assert.AreEqual("/willkommen?einrichten=passkey", query["returnUrl"].ToString());

        var verify = await _client.PostAsJsonAsync("/api/auth/login-link/verify", new VerifyLoginLinkDto
        {
            UserId = Guid.Parse(query["uid"]!),
            Token = query["token"]!
        });
        Assert.AreEqual(HttpStatusCode.OK, verify.StatusCode);

        // The invitation link cannot be used again.
        var again = await _client.GetAsync($"/api/invitations/details?token={Uri.EscapeDataString(invitationToken)}");
        Assert.AreEqual(HttpStatusCode.NotFound, again.StatusCode);
    }

    private async Task<string> SeedInvitationAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VernetztDbContext>();
        var admin = await db.Users.FirstAsync(u => u.Email == AdminEmail);
        var token = $"invite-{Guid.NewGuid():N}";
        db.InvitationTokens.Add(new InvitationToken
        {
            Id = Guid.NewGuid(),
            Email = email,
            Token = token,
            InvitedById = admin.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        });
        await db.SaveChangesAsync();
        return token;
    }

    /// <summary>
    /// Origin accepted by the CSRF guard for cookie-authenticated unsafe requests (App:BaseUrl).
    /// </summary>
    private string ConfiguredOrigin() =>
        new Uri(_factory.Services.GetRequiredService<IConfiguration>()["App:BaseUrl"]!).GetLeftPart(UriPartial.Authority);

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = BaseAddress,
        HandleCookies = true,
        AllowAutoRedirect = false
    });

    private async Task<(Guid UserId, string Token)> RequestAndCaptureLinkAsync()
    {
        // Requested from a fresh, anonymous browser (like the login page before sign-in).
        var before = _sentLoginLinks.Count;
        using var anonymous = CreateClient();
        var response = await anonymous.PostAsJsonAsync("/api/auth/login-link/request", new LoginLinkRequestDto { Email = AdminEmail });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.HasCount(before + 1, _sentLoginLinks);

        var query = QueryHelpers.ParseQuery(new Uri(_sentLoginLinks[^1].Url).Query);
        return (Guid.Parse(query["uid"]!), query["token"]!);
    }

    private Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Origin", ConfiguredOrigin());
        if (method == HttpMethod.Post)
        {
            request.Content = JsonContent.Create(new { credentialJson = "{}" });
        }

        return client.SendAsync(request);
    }

    private static async Task AssertPasskeyRequiredAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: {body}");
        StringAssert.Contains(body, "PASSKEY_REQUIRED");
    }

    private static async Task<JsonElement> ReadStatusAsync(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<ApiResponse<JsonElement>>("/api/auth/status");
        Assert.IsNotNull(response);
        return response.Data;
    }
}
