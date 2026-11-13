using System.Net;
using System.Net.Http.Json;
using Igevo.Vernetzt.Core.DTOs.Public;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Igevo.Vernetzt.Tests.Integration;

[TestClass]
public sealed class PublicDirectoryControllerIntegrationTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private Guid _clubId;
    private Guid _departmentId;
    private Guid _clubEventId;
    private Guid _departmentEventId;
    private Guid _publicContactId;
    private Guid _privateContactId;

    [TestInitialize]
    public async Task Setup()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Test"));
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VernetztDbContext>();

        var club = new Club
        {
            Id = Guid.NewGuid(),
            Name = "Öffentlicher Sportverein",
            City = "Berlin",
            Email = "verein@example.org",
            WebsiteUrl = "https://example.org",
            SocialMediaLinks =
            [
                new SocialMediaLink(SocialMediaPlatform.Instagram, "https://instagram.com/sportverein"),
                new SocialMediaLink(SocialMediaPlatform.Facebook, "https://facebook.com/sportverein")
            ],
            ContactPersonsVisibility = VisibilityLevel.Public
        };
        var department = new Department
        {
            Id = Guid.NewGuid(),
            Name = "Jugend",
            Club = club,
            ClubId = club.Id,
            ContactPersonsVisibility = VisibilityLevel.Public
        };
        var publicUser = new User
        {
            Id = Guid.NewGuid(), FirstName = "Petra", LastName = "Publik",
            Email = "petra@example.org", EmailVisibility = VisibilityLevel.Public,
            PhoneNumber = "+49 30 123", PhoneVisibility = VisibilityLevel.Public
        };
        var privateUser = new User
        {
            Id = Guid.NewGuid(), FirstName = "Ina", LastName = "Intern",
            Email = "ina@example.org", EmailVisibility = VisibilityLevel.Internal,
            PhoneNumber = "+49 30 999", PhoneVisibility = VisibilityLevel.Internal
        };
        db.AddRange(club, department, publicUser, privateUser);
        var publicAssignment = new ContactAssignment
        {
            Id = Guid.NewGuid(), Club = club, ClubId = club.Id, User = publicUser, UserId = publicUser.Id,
            ContextEmail = "unverified@example.org", ContextEmailVisibility = VisibilityLevel.Public,
            ContextEmailVerified = false
        };
        var privateAssignment = new ContactAssignment
        {
            Id = Guid.NewGuid(), Club = club, ClubId = club.Id, User = privateUser, UserId = privateUser.Id
        };
        db.ContactAssignments.AddRange(publicAssignment, privateAssignment);

        var now = DateTime.UtcNow;
        var clubEvent = new Event
        {
            Id = Guid.NewGuid(), Title = "Öffentliches Fest", Club = club, ClubId = club.Id,
            StartUtc = now.AddDays(1), EndUtc = now.AddDays(1).AddHours(2)
        };
        var departmentEvent = new Event
        {
            Id = Guid.NewGuid(), Title = "Schnuppertraining", Department = department, DepartmentId = department.Id,
            StartUtc = now.AddDays(2), EndUtc = now.AddDays(2).AddHours(1)
        };
        db.Events.AddRange(clubEvent, departmentEvent);
        await db.SaveChangesAsync();

        _clubId = club.Id;
        _departmentId = department.Id;
        _clubEventId = clubEvent.Id;
        _departmentEventId = departmentEvent.Id;
        _publicContactId = publicAssignment.Id;
        _privateContactId = privateAssignment.Id;
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [TestMethod]
    public async Task Clubs_AreAnonymousPagedAndCacheable()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/public/v1/clubs?query=Sport&pageSize=10");
        request.Headers.Add("Origin", "https://public.example.org");
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("*", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.IsTrue(response.Headers.CacheControl?.Public);
        Assert.AreEqual(TimeSpan.FromSeconds(60), response.Headers.CacheControl?.MaxAge);

        var result = await response.Content.ReadFromJsonAsync<PublicPagedResult<PublicClubSummaryDto>>();
        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.TotalCount);
        Assert.AreEqual(_clubId, result.Items.Single().Id);
    }

    [TestMethod]
    public async Task RandomClubs_ReturnRequestedCountInVaryingOrderWithoutCaching()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VernetztDbContext>();
            db.Clubs.AddRange(Enumerable.Range(1, 11).Select(i => new Club { Id = Guid.NewGuid(), Name = $"Verein {i:00}" }));
            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync("/api/public/v1/clubs/random?count=5");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(response.Headers.CacheControl?.NoStore);
        var result = await response.Content.ReadFromJsonAsync<PublicPagedResult<PublicClubSummaryDto>>();
        Assert.IsNotNull(result);
        Assert.AreEqual(12, result.TotalCount);
        Assert.AreEqual(5, result.Items.Count);
        Assert.AreEqual(5, result.Items.Select(c => c.Id).Distinct().Count());

        // Twelve clubs give 12!/7! possible draws, so repeated calls must not all match the first one.
        var first = string.Join(',', result.Items.Select(c => c.Id));
        var draws = new HashSet<string> { first };
        for (var i = 0; i < 10; i++)
        {
            var next = await _client.GetFromJsonAsync<PublicPagedResult<PublicClubSummaryDto>>("/api/public/v1/clubs/random?count=5");
            draws.Add(string.Join(',', next!.Items.Select(c => c.Id)));
        }
        Assert.IsTrue(draws.Count > 1, "Random clubs must change between requests.");
    }

    [TestMethod]
    public async Task Club_OnlyFlagsExplicitlyPublicContactFields()
    {
        var response = await _client.GetAsync($"/api/public/v1/clubs/{_clubId}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var club = await response.Content.ReadFromJsonAsync<PublicClubDto>();

        Assert.IsNotNull(club);
        Assert.IsTrue(club.HasEmail);
        Assert.AreEqual(2, club.Contacts.Count);
        var publicContact = club.Contacts.Single(c => c.LastName == "Publik");
        Assert.AreEqual(_publicContactId, publicContact.Id);
        Assert.IsTrue(publicContact.HasEmail);
        Assert.IsTrue(publicContact.HasPhoneNumber);
        var privateContact = club.Contacts.Single(c => c.LastName == "Intern");
        Assert.IsFalse(privateContact.HasEmail);
        Assert.IsFalse(privateContact.HasPhoneNumber);
        Assert.AreEqual(_departmentId, club.Departments.Single().Id);
    }

    [TestMethod]
    public async Task ClubAndDepartmentDetails_NeverContainEmailAddressesOrPhoneNumbers()
    {
        var club = await _client.GetStringAsync($"/api/public/v1/clubs/{_clubId}");
        var department = await _client.GetStringAsync($"/api/public/v1/departments/{_departmentId}");

        foreach (var body in new[] { club, department })
        {
            Assert.IsFalse(body.Contains('@'), $"Public detail must not contain an email address: {body}");
            Assert.IsFalse(body.Contains("+49", StringComparison.Ordinal), $"Public detail must not contain a phone number: {body}");
        }
    }

    [TestMethod]
    public async Task ClubContactDetails_RevealEmailWithoutCaching()
    {
        var response = await _client.GetAsync($"/api/public/v1/clubs/{_clubId}/contact-details");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(response.Headers.CacheControl?.NoStore);
        var details = await response.Content.ReadFromJsonAsync<PublicContactDetailsDto>();
        Assert.IsNotNull(details);
        Assert.AreEqual("verein@example.org", details.Email);
        Assert.IsNull(details.PhoneNumber);
    }

    [TestMethod]
    public async Task DepartmentContactDetails_Return404WithoutEmail()
    {
        Assert.AreEqual(HttpStatusCode.NotFound,
            (await _client.GetAsync($"/api/public/v1/departments/{_departmentId}/contact-details")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound,
            (await _client.GetAsync($"/api/public/v1/departments/{Guid.NewGuid()}/contact-details")).StatusCode);
    }

    [TestMethod]
    public async Task ContactDetails_RevealOnlyExplicitlyPublicFields()
    {
        var details = await _client.GetFromJsonAsync<PublicContactDetailsDto>(
            $"/api/public/v1/contacts/{_publicContactId}/contact-details");

        Assert.IsNotNull(details);
        Assert.AreEqual("petra@example.org", details.Email,
            "An unverified context email must never replace a public primary email.");
        Assert.AreEqual("+49 30 123", details.PhoneNumber);
        Assert.AreEqual(HttpStatusCode.NotFound,
            (await _client.GetAsync($"/api/public/v1/contacts/{_privateContactId}/contact-details")).StatusCode);
    }

    [TestMethod]
    public async Task ContactDetails_Return404WhenContactPersonsAreNotPublic()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VernetztDbContext>();
            var club = await db.Clubs.FindAsync(_clubId);
            club!.ContactPersonsVisibility = VisibilityLevel.Internal;
            await db.SaveChangesAsync();
        }

        Assert.AreEqual(HttpStatusCode.NotFound,
            (await _client.GetAsync($"/api/public/v1/contacts/{_publicContactId}/contact-details")).StatusCode);
    }

    [TestMethod]
    public async Task ContactDetails_HaveOwnStricterRateLimit()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("RateLimiting:PublicContactReveal:PermitLimit", "2");
        });
        using var client = factory.CreateClient();
        var id = Guid.NewGuid();

        Assert.AreEqual(HttpStatusCode.NotFound, (await client.GetAsync($"/api/public/v1/clubs/{id}/contact-details")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await client.GetAsync($"/api/public/v1/contacts/{id}/contact-details")).StatusCode);
        Assert.AreEqual(HttpStatusCode.TooManyRequests,
            (await client.GetAsync($"/api/public/v1/departments/{id}/contact-details")).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync("/api/public/v1/club-types")).StatusCode,
            "Revealing contact details must not use up the general public API budget.");
    }

    [TestMethod]
    public async Task Club_ReturnsSocialMediaLinksInStoredOrder()
    {
        var club = await _client.GetFromJsonAsync<PublicClubDto>($"/api/public/v1/clubs/{_clubId}");

        Assert.IsNotNull(club);
        CollectionAssert.AreEqual(
            new[] { SocialMediaPlatform.Instagram, SocialMediaPlatform.Facebook },
            club.SocialMediaLinks.Select(l => l.Platform).ToArray());
        Assert.AreEqual("https://instagram.com/sportverein", club.SocialMediaLinks[0].Url);

        var department = await _client.GetFromJsonAsync<PublicDepartmentDto>($"/api/public/v1/departments/{_departmentId}");
        Assert.IsNotNull(department);
        // A department without own links shows the club's links and names them as inherited.
        CollectionAssert.AreEqual(
            club.SocialMediaLinks.Select(l => l.Url).ToArray(),
            department.SocialMediaLinks.Select(l => l.Url).ToArray());
        Assert.AreEqual(PublicInheritedSourceDto.ClubKind, department.SocialMediaSource?.Kind);
        Assert.AreEqual(_clubId, department.SocialMediaSource?.Id);
    }

    [TestMethod]
    public async Task Events_ListsAllClubAndDepartmentEventsWithDetails()
    {
        var listResponse = await _client.GetAsync("/api/public/v1/events?pageSize=100");
        var result = await listResponse.Content.ReadFromJsonAsync<PublicPagedResult<PublicEventDto>>();

        Assert.IsNotNull(result);
        Assert.IsTrue(result.Items.Any(e => e.Id == _clubEventId));
        Assert.IsTrue(result.Items.Any(e => e.Id == _departmentEventId));
        Assert.AreEqual(HttpStatusCode.OK,
            (await _client.GetAsync($"/api/public/v1/events/{_departmentEventId}")).StatusCode);
    }

    [TestMethod]
    public async Task PublicApi_AppliesConfiguredRateLimit()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("RateLimiting:PublicApi:PermitLimit", "2");
        });
        using var client = factory.CreateClient();

        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync("/api/public/v1/club-types?request=1")).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync("/api/public/v1/club-types?request=2")).StatusCode);
        Assert.AreEqual(HttpStatusCode.TooManyRequests,
            (await client.GetAsync("/api/public/v1/club-types?request=3")).StatusCode);
    }

    [TestMethod]
    public async Task OpenApiDocument_ContainsOnlyPublicContract()
    {
        var response = await _client.GetAsync("/api/public/v1/openapi.json");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var document = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(document, "/api/public/v1/clubs");
        Assert.IsFalse(document.Contains("/api/users", StringComparison.Ordinal));
    }
}
