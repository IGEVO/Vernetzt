using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Xml.Linq;
using Igevo.Vernetzt.Core.DTOs;
using Igevo.Vernetzt.Core.DTOs.CardDav;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Igevo.Vernetzt.Tests.Integration;

[TestClass]
public class CardDavIntegrationTests
{
    private static readonly XNamespace Dav = "DAV:";
    private static readonly XNamespace Card = "urn:ietf:params:xml:ns:carddav";
    private static readonly XNamespace CalendarServer = "http://calendarserver.org/ns/";

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private Guid _userId;
    private Guid _otherUserId;
    private string _email = null!;

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
                        TestHeaderAuthenticationHandler.SchemeName, _ => { });
                });
            });
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VernetztDbContext>();
        _userId = Guid.NewGuid();
        _otherUserId = Guid.NewGuid();
        _email = $"carddav-{_userId:N}@test.local";
        db.Users.AddRange(
            new User { Id = _userId, FirstName = "Carla", LastName = "Dav", Email = _email, EmailVerified = true },
            new User { Id = _otherUserId, FirstName = "Otto", LastName = "Other", Email = $"other-{_otherUserId:N}@test.local", PhoneNumber = "0301234" });
        await db.SaveChangesAsync();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    // ------------------------------------------------------------ token API

    [TestMethod]
    public async Task TokenApi_CreateStatusRevoke()
    {
        var status = await GetStatusAsync();
        Assert.IsFalse(status.HasAccess);
        Assert.AreEqual(_email, status.Username);
        StringAssert.EndsWith(status.ServerUrl, "/carddav/");

        var password = await CreatePasswordAsync();
        Assert.IsTrue((await GetStatusAsync()).HasAccess);

        using var revoke = Authenticated(HttpMethod.Delete, "/api/users/me/carddav/token");
        Assert.AreEqual(HttpStatusCode.NoContent, (await _client.SendAsync(revoke)).StatusCode);

        using var propfind = DavRequest(HttpMethod.Get, "/carddav/", password);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await _client.SendAsync(propfind)).StatusCode);
    }

    [TestMethod]
    public async Task TokenApi_WithoutSession_ReturnsUnauthorized()
    {
        var response = await _client.PostAsync("/api/users/me/carddav/token", null);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ------------------------------------------------------------ DAV

    [TestMethod]
    public async Task Propfind_WithoutCredentials_ChallengesWithBasic()
    {
        using var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), "/carddav/");
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        StringAssert.StartsWith(response.Headers.WwwAuthenticate.ToString(), "Basic");
    }

    [TestMethod]
    public async Task Propfind_WithWrongPassword_ReturnsUnauthorized()
    {
        await CreatePasswordAsync();
        using var request = DavRequest(new HttpMethod("PROPFIND"), "/carddav/", "wrong-password");

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await _client.SendAsync(request)).StatusCode);
    }

    [TestMethod]
    public async Task WellKnown_RedirectsToCardDavRoot()
    {
        using var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), "/.well-known/carddav");
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.AreEqual("/carddav/", response.Headers.Location?.OriginalString);
    }

    [TestMethod]
    public async Task Options_AdvertisesAddressbookWithoutAuthentication()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/carddav/");
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        StringAssert.Contains(string.Join(",", response.Headers.GetValues("DAV")), "addressbook");
    }

    [TestMethod]
    public async Task Discovery_FromRootToAddressBook()
    {
        var password = await CreatePasswordAsync();

        var root = await PropfindAsync("/carddav/", password, "0",
            "<d:propfind xmlns:d=\"DAV:\"><d:prop><d:current-user-principal/></d:prop></d:propfind>");
        var principalHref = root.Descendants(Dav + "current-user-principal").Single().Element(Dav + "href")!.Value;
        Assert.AreEqual($"/carddav/principals/{_userId}/", principalHref);

        var principal = await PropfindAsync(principalHref, password, "0",
            "<d:propfind xmlns:d=\"DAV:\" xmlns:c=\"urn:ietf:params:xml:ns:carddav\"><d:prop><c:addressbook-home-set/></d:prop></d:propfind>");
        var homeHref = principal.Descendants(Card + "addressbook-home-set").Single().Element(Dav + "href")!.Value;

        var home = await PropfindAsync(homeHref, password, "1",
            "<d:propfind xmlns:d=\"DAV:\" xmlns:cs=\"http://calendarserver.org/ns/\"><d:prop><d:resourcetype/><d:displayname/><cs:getctag/></d:prop></d:propfind>");
        var bookResponse = home.Descendants(Dav + "response")
            .Single(r => r.Descendants(Card + "addressbook").Any());
        Assert.AreEqual($"/carddav/addressbooks/{_userId}/vernetzt/", bookResponse.Element(Dav + "href")!.Value);
        Assert.IsFalse(string.IsNullOrWhiteSpace(bookResponse.Descendants(CalendarServer + "getctag").Single().Value));
    }

    [TestMethod]
    public async Task AddressBook_Depth1ListsCardsAndMultigetReturnsVCards()
    {
        var password = await CreatePasswordAsync();
        var bookHref = $"/carddav/addressbooks/{_userId}/vernetzt/";

        var listing = await PropfindAsync(bookHref, password, "1",
            "<d:propfind xmlns:d=\"DAV:\"><d:prop><d:getetag/><d:resourcetype/></d:prop></d:propfind>");
        var cardHrefs = listing.Descendants(Dav + "response")
            .Select(r => r.Element(Dav + "href")!.Value)
            .Where(h => h.EndsWith(".vcf", StringComparison.Ordinal))
            .ToList();
        CollectionAssert.Contains(cardHrefs, $"{bookHref}{_otherUserId}.vcf");

        var multiget = $"""
            <c:addressbook-multiget xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:carddav">
              <d:prop><d:getetag/><c:address-data/></d:prop>
              <d:href>{bookHref}{_otherUserId}.vcf</d:href>
              <d:href>{bookHref}does-not-exist.vcf</d:href>
            </c:addressbook-multiget>
            """;
        using var report = DavRequest(new HttpMethod("REPORT"), bookHref, password, multiget);
        report.Headers.Add("Depth", "1");
        var response = await _client.SendAsync(report);
        Assert.AreEqual(HttpStatusCode.MultiStatus, response.StatusCode);

        var xml = XDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = xml.Descendants(Card + "address-data").Single().Value;
        StringAssert.Contains(data, "FN:Otto Other");
        StringAssert.Contains(data, "TEL;TYPE=VOICE:0301234");
        Assert.IsTrue(xml.Descendants(Dav + "status").Any(s => s.Value.Contains("404", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task GetCard_ReturnsVCardWithETagAndSupportsNotModified()
    {
        var password = await CreatePasswordAsync();
        var href = $"/carddav/addressbooks/{_userId}/vernetzt/{_otherUserId}.vcf";

        using var get = DavRequest(HttpMethod.Get, href, password);
        var response = await _client.SendAsync(get);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("text/vcard", response.Content.Headers.ContentType?.MediaType);
        var etag = response.Headers.ETag;
        Assert.IsNotNull(etag);
        StringAssert.StartsWith(await response.Content.ReadAsStringAsync(), "BEGIN:VCARD");

        using var conditional = DavRequest(HttpMethod.Get, href, password);
        conditional.Headers.IfNoneMatch.Add(etag!);
        Assert.AreEqual(HttpStatusCode.NotModified, (await _client.SendAsync(conditional)).StatusCode);
    }

    [TestMethod]
    public async Task WritingMethods_AreForbidden()
    {
        var password = await CreatePasswordAsync();
        var href = $"/carddav/addressbooks/{_userId}/vernetzt/{_otherUserId}.vcf";

        using var put = DavRequest(HttpMethod.Put, href, password, "BEGIN:VCARD\r\nEND:VCARD\r\n");
        Assert.AreEqual(HttpStatusCode.Forbidden, (await _client.SendAsync(put)).StatusCode);

        using var delete = DavRequest(HttpMethod.Delete, href, password);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await _client.SendAsync(delete)).StatusCode);
    }

    [TestMethod]
    public async Task ForeignAddressBook_IsForbidden()
    {
        var password = await CreatePasswordAsync();
        using var request = DavRequest(new HttpMethod("PROPFIND"), $"/carddav/addressbooks/{_otherUserId}/vernetzt/", password);
        request.Headers.Add("Depth", "0");

        Assert.AreEqual(HttpStatusCode.Forbidden, (await _client.SendAsync(request)).StatusCode);
    }

    [TestMethod]
    public async Task Propfind_WithDtd_IsRejected()
    {
        var password = await CreatePasswordAsync();
        const string body = """<?xml version="1.0"?><!DOCTYPE x [<!ENTITY e SYSTEM "file:///etc/passwd">]><d:propfind xmlns:d="DAV:"><d:prop>&e;</d:prop></d:propfind>""";
        using var request = DavRequest(new HttpMethod("PROPFIND"), "/carddav/", password, body);

        Assert.AreEqual(HttpStatusCode.BadRequest, (await _client.SendAsync(request)).StatusCode);
    }

    // ------------------------------------------------------------ security boundaries

    [TestMethod]
    public async Task BasicCredentials_DoNotAuthenticateApiEndpoints()
    {
        var password = await CreatePasswordAsync();

        // The CardDAV app password only works on /carddav: the API ignores Basic credentials.
        using var me = DavRequest(HttpMethod.Get, "/api/users/me", password);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await _client.SendAsync(me)).StatusCode);

        using var contacts = DavRequest(HttpMethod.Get, "/api/users/contacts", password);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await _client.SendAsync(contacts)).StatusCode);

        // Not even a new app password can be created with it.
        using var newToken = DavRequest(HttpMethod.Post, "/api/users/me/carddav/token", password);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await _client.SendAsync(newToken)).StatusCode);
    }

    [TestMethod]
    public async Task SuccessfulLogin_ResetsFailureCounter()
    {
        var password = await CreatePasswordAsync();
        var failuresBeforeLockout = Igevo.Vernetzt.Server.Identity.CardDavBasicAuthenticationHandler.MaxFailuresPerWindow - 1;

        // Two rounds of typos, each just below the limit, separated by a successful login: no lockout.
        for (var round = 0; round < 2; round++)
        {
            for (var i = 0; i < failuresBeforeLockout; i++)
            {
                using var wrong = DavRequest(new HttpMethod("PROPFIND"), "/carddav/", "wrong-password");
                Assert.AreEqual(HttpStatusCode.Unauthorized, (await _client.SendAsync(wrong)).StatusCode);
            }

            using var correct = DavRequest(new HttpMethod("PROPFIND"), "/carddav/", password);
            correct.Headers.Add("Depth", "0");
            Assert.AreEqual(HttpStatusCode.MultiStatus, (await _client.SendAsync(correct)).StatusCode);
        }
    }

    [TestMethod]
    public async Task RepeatedFailures_AreRejectedWithTooManyRequests()
    {
        var password = await CreatePasswordAsync();
        for (var i = 0; i < Igevo.Vernetzt.Server.Identity.CardDavBasicAuthenticationHandler.MaxFailuresPerWindow; i++)
        {
            using var wrong = DavRequest(new HttpMethod("PROPFIND"), "/carddav/", "wrong-password");
            await _client.SendAsync(wrong);
        }

        using var correct = DavRequest(new HttpMethod("PROPFIND"), "/carddav/", password);
        Assert.AreEqual((HttpStatusCode)429, (await _client.SendAsync(correct)).StatusCode);
    }

    [TestMethod]
    public async Task LogoutOnAllDevices_RevokesCardDavAccess()
    {
        var password = await CreatePasswordAsync();

        using var logoutAll = Authenticated(HttpMethod.Post, "/api/auth/logout-all");
        var response = await _client.SendAsync(logoutAll);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());

        using var propfind = DavRequest(new HttpMethod("PROPFIND"), "/carddav/", password);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await _client.SendAsync(propfind)).StatusCode);
        Assert.IsFalse((await GetStatusAsync()).HasAccess);
    }

    // ------------------------------------------------------------ helpers

    private async Task<CardDavStatusDto> GetStatusAsync()
    {
        using var request = Authenticated(HttpMethod.Get, "/api/users/me/carddav");
        var response = await _client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<CardDavStatusDto>>())!.Data!;
    }

    private async Task<string> CreatePasswordAsync()
    {
        using var request = Authenticated(HttpMethod.Post, "/api/users/me/carddav/token");
        var response = await _client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<CardDavAccessCreatedDto>>();
        return payload!.Data!.Password;
    }

    private async Task<XDocument> PropfindAsync(string href, string password, string depth, string body)
    {
        using var request = DavRequest(new HttpMethod("PROPFIND"), href, password, body);
        request.Headers.Add("Depth", depth);
        var response = await _client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.MultiStatus, response.StatusCode, await response.Content.ReadAsStringAsync());
        return XDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private HttpRequestMessage Authenticated(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestHeaderAuthenticationHandler.UserIdHeaderName, _userId.ToString());
        return request;
    }

    private HttpRequestMessage DavRequest(HttpMethod method, string url, string password, string? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_email}:{password}")));
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/xml");
        }

        return request;
    }
}
