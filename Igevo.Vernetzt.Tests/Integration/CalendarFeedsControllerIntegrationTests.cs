using System.Net;
using System.Net.Http.Json;
using Igevo.Vernetzt.Core.DTOs;
using Igevo.Vernetzt.Core.DTOs.Events;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Igevo.Vernetzt.Tests.Integration;

[TestClass]
public class CalendarFeedsControllerIntegrationTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    private Guid _adminUserId;
    private Guid _regularUserId;

    [TestInitialize]
    public async Task Setup()
    {
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
                });
            });

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        await SeedUsersAsync();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client?.Dispose();
        _factory?.Dispose();
    }

    [TestMethod]
    public async Task CreateToken_WithoutAuthentication_ReturnsUnauthorized()
    {
        var dto = new CreateCalendarFeedTokenDto
        {
            Scope = CalendarFeedScope.Global,
        };

        var response = await _client.PostAsJsonAsync("/api/calendar-feeds/tokens", dto);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateGlobalToken_RegularUser_ReturnsForbidden()
    {
        var dto = new CreateCalendarFeedTokenDto
        {
            Scope = CalendarFeedScope.Global,
            HighlightsOnly = true
        };

        using var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/calendar-feeds/tokens", _regularUserId, dto);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateGlobalToken_Admin_ReturnsCreatedWithFeedUrl()
    {
        var dto = new CreateCalendarFeedTokenDto
        {
            Scope = CalendarFeedScope.Global,
            HighlightsOnly = false
        };

        using var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/calendar-feeds/tokens", _adminUserId, dto);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<CalendarFeedTokenCreatedDto>>();
        Assert.IsNotNull(payload);
        Assert.IsTrue(payload!.Success);
        Assert.IsNotNull(payload.Data);
        Assert.IsTrue(payload.Data!.FeedUrl.Contains("/api/calendar-feeds/", StringComparison.Ordinal));
        Assert.IsTrue(payload.Data.FeedUrl.EndsWith(".ics", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task GetIcs_WithValidToken_ReturnsCalendarFile()
    {
        var dto = new CreateCalendarFeedTokenDto
        {
            Scope = CalendarFeedScope.Global,
            HighlightsOnly = false
        };

        using var createRequest = CreateAuthenticatedRequest(HttpMethod.Post, "/api/calendar-feeds/tokens", _adminUserId, dto);
        var createResponse = await _client.SendAsync(createRequest);

        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);

        var payload = await createResponse.Content.ReadFromJsonAsync<ApiResponse<CalendarFeedTokenCreatedDto>>();
        Assert.IsNotNull(payload);
        Assert.IsNotNull(payload!.Data);

        var token = ExtractTokenFromFeedUrl(payload.Data!.FeedUrl);
        var feedResponse = await _client.GetAsync($"/api/calendar-feeds/{token}.ics");

        Assert.AreEqual(HttpStatusCode.OK, feedResponse.StatusCode);
        Assert.AreEqual("text/calendar", feedResponse.Content.Headers.ContentType?.MediaType);

        var calendar = await feedResponse.Content.ReadAsStringAsync();
        Assert.IsTrue(calendar.Contains("BEGIN:VCALENDAR", StringComparison.Ordinal));
        Assert.IsTrue(calendar.Contains("END:VCALENDAR", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task GetIcs_InvalidToken_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/calendar-feeds/invalid-token.ics");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static HttpRequestMessage CreateAuthenticatedRequest<T>(HttpMethod method, string endpoint, Guid userId, T payload)
    {
        var request = new HttpRequestMessage(method, endpoint)
        {
            Content = JsonContent.Create(payload)
        };

        request.Headers.Add(TestHeaderAuthenticationHandler.UserIdHeaderName, userId.ToString());
        return request;
    }

    private static string ExtractTokenFromFeedUrl(string feedUrl)
    {
        var marker = "/api/calendar-feeds/";
        var markerIndex = feedUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            throw new InvalidOperationException("Feed URL does not contain calendar feed path.");
        }

        var start = markerIndex + marker.Length;
        var suffix = ".ics";
        var end = feedUrl.LastIndexOf(suffix, StringComparison.OrdinalIgnoreCase);
        if (end <= start)
        {
            throw new InvalidOperationException("Feed URL does not contain a valid token segment.");
        }

        return feedUrl[start..end];
    }

    private async Task SeedUsersAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VernetztDbContext>();

        _adminUserId = Guid.NewGuid();
        _regularUserId = Guid.NewGuid();

        db.Users.AddRange(
            new User
            {
                Id = _adminUserId,
                FirstName = "Calendar",
                LastName = "Admin",
                Email = $"calendar-admin-{_adminUserId:N}@test.local",
                EmailVerified = true,
                Claims = [UserClaim.SystemAdmin]
            },
            new User
            {
                Id = _regularUserId,
                FirstName = "Calendar",
                LastName = "User",
                Email = $"calendar-user-{_regularUserId:N}@test.local",
                EmailVerified = true,
                Claims = []
            });

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }
}
