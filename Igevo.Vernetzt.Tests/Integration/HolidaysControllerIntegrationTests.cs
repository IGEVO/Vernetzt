using System.Net;
using System.Net.Http.Json;
using Igevo.Vernetzt.Core.DTOs;
using Igevo.Vernetzt.Core.DTOs.Events;
using Igevo.Vernetzt.Core.DTOs.Holidays;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static Igevo.Vernetzt.Tests.Services.HolidayIcsParserTests;

namespace Igevo.Vernetzt.Tests.Integration;

/// <summary>
/// Holiday calendar: readable by every signed-in user, maintained by admins and managers,
/// never part of the iCal exports.
/// </summary>
[TestClass]
public sealed class HolidaysControllerIntegrationTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private readonly Guid _adminId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    private const string FeedContent =
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Test//DE\r\n" +
        "BEGIN:VEVENT\r\nUID:h1\r\nDTSTART;VALUE=DATE:20261012\r\nDTEND;VALUE=DATE:20261025\r\nSUMMARY:Herbstferien Abo\r\nEND:VEVENT\r\n" +
        "END:VCALENDAR\r\n";

    [TestInitialize]
    public async Task Setup()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
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
                        TestHeaderAuthenticationHandler.SchemeName, _ => { });
                services.RemoveAll<IHolidayFeedFetcher>();
                services.AddSingleton<IHolidayFeedFetcher>(new StaticFetcher(FeedContent));
            });
        });
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VernetztDbContext>();
        db.Users.AddRange(
            new User { Id = _adminId, FirstName = "A", LastName = "Admin", Email = $"a-{_adminId:N}@test.local", EmailVerified = true, Claims = [UserClaim.SystemAdmin] },
            new User { Id = _userId, FirstName = "U", LastName = "User", Email = $"u-{_userId:N}@test.local", EmailVerified = true });
        await db.SaveChangesAsync();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [TestMethod]
    public async Task GetEntries_RequiresAuthentication()
    {
        var response = await _client.GetAsync("/api/holidays?from=2026-10-01&to=2026-10-31");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task RegularUser_CanReadButNotMaintain()
    {
        var create = await SendAsync(HttpMethod.Post, "/api/holidays", _userId, null,
            new SaveHolidayEntryDto { Title = "X", StartDate = new(2026, 10, 3), EndDate = new(2026, 10, 3) });
        Assert.AreEqual(HttpStatusCode.Forbidden, create.StatusCode);

        var sources = await SendAsync(HttpMethod.Get, "/api/holidays/sources", _userId, null);
        Assert.AreEqual(HttpStatusCode.Forbidden, sources.StatusCode);

        var read = await SendAsync(HttpMethod.Get, "/api/holidays?from=2026-10-01&to=2026-10-31", _userId, null);
        Assert.AreEqual(HttpStatusCode.OK, read.StatusCode);
    }

    [TestMethod]
    public async Task Manager_MaintainsEntriesAndSubscriptions()
    {
        var create = await SendAsync(HttpMethod.Post, "/api/holidays", _adminId, "Manager",
            new SaveHolidayEntryDto { Title = "Tag der Deutschen Einheit", StartDate = new(2026, 10, 3), EndDate = new(2026, 10, 3) });
        Assert.AreEqual(HttpStatusCode.OK, create.StatusCode);

        var source = await SendAsync(HttpMethod.Post, "/api/holidays/sources", _adminId, "Manager",
            new SaveHolidaySourceDto { Name = "Ferien", Url = "https://example.org/ferien.ics" });
        Assert.AreEqual(HttpStatusCode.OK, source.StatusCode);
        var sourceDto = (await source.Content.ReadFromJsonAsync<ApiResponse<HolidaySourceDto>>())!.Data!;
        Assert.AreEqual(1, sourceDto.EntryCount);

        var read = await SendAsync(HttpMethod.Get, "/api/holidays?from=2026-10-01&to=2026-10-31", _userId, null);
        var entries = (await read.Content.ReadFromJsonAsync<ApiResponse<List<HolidayEntryDto>>>())!.Data!;
        CollectionAssert.AreEqual(
            new[] { "Tag der Deutschen Einheit", "Herbstferien Abo" },
            entries.Select(e => e.Title).ToArray());
        Assert.AreEqual(new DateOnly(2026, 10, 24), entries[1].EndDate);
    }

    [TestMethod]
    public async Task InvalidInput_ReturnsBadRequest()
    {
        var range = await SendAsync(HttpMethod.Get, "/api/holidays?from=2026-10-31&to=2026-10-01", _userId, null);
        Assert.AreEqual(HttpStatusCode.BadRequest, range.StatusCode);

        var url = await SendAsync(HttpMethod.Post, "/api/holidays/sources", _adminId, "SystemAdmin",
            new SaveHolidaySourceDto { Name = "Intern", Url = "http://intranet/ferien.ics" });
        Assert.AreEqual(HttpStatusCode.BadRequest, url.StatusCode);
    }

    [TestMethod]
    public async Task Holidays_AreNotPartOfTheICalExport()
    {
        await SendAsync(HttpMethod.Post, "/api/holidays", _adminId, "SystemAdmin",
            new SaveHolidayEntryDto { Title = "Manueller Feiertag", StartDate = new(2026, 10, 3), EndDate = new(2026, 10, 3) });
        await SendAsync(HttpMethod.Post, "/api/holidays/sources", _adminId, "SystemAdmin",
            new SaveHolidaySourceDto { Name = "Ferien", Url = "https://example.org/ferien.ics" });

        var token = await SendAsync(HttpMethod.Post, "/api/calendar-feeds/tokens", _adminId, "SystemAdmin",
            new CreateCalendarFeedTokenDto { Scope = CalendarFeedScope.Global });
        Assert.AreEqual(HttpStatusCode.Created, token.StatusCode);
        var feedUrl = (await token.Content.ReadFromJsonAsync<ApiResponse<CalendarFeedTokenCreatedDto>>())!.Data!.FeedUrl;

        var ics = await _client.GetStringAsync(new Uri(feedUrl).PathAndQuery);

        StringAssert.Contains(ics, "BEGIN:VCALENDAR");
        Assert.IsFalse(ics.Contains("Manueller Feiertag", StringComparison.Ordinal));
        Assert.IsFalse(ics.Contains("Herbstferien", StringComparison.Ordinal));
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, Guid userId, string? roles, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestHeaderAuthenticationHandler.UserIdHeaderName, userId.ToString());
        if (roles is not null)
        {
            request.Headers.Add(TestHeaderAuthenticationHandler.RolesHeaderName, roles);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType());
        }

        return await _client.SendAsync(request);
    }

    private sealed class StaticFetcher(string content) : IHolidayFeedFetcher
    {
        public Task<string> FetchAsync(Uri url, CancellationToken cancellationToken = default) => Task.FromResult(content);
    }
}
