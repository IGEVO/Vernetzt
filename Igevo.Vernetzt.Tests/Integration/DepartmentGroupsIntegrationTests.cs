using System.Net;
using System.Net.Http.Json;
using Igevo.Vernetzt.Core.DTOs;
using Igevo.Vernetzt.Core.DTOs.Departments;
using Igevo.Vernetzt.Core.DTOs.Events;
using Igevo.Vernetzt.Core.DTOs.Public;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Igevo.Vernetzt.Tests.Integration;

/// <summary>
/// Groups are departments below a department (club &gt; department &gt; group).
/// Covers creation rules, inherited edit rights, event aggregation, deletion and the public API.
/// </summary>
[TestClass]
public class DepartmentGroupsIntegrationTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    private Guid _clubEditorId;
    private Guid _departmentEditorId;
    private Guid _groupEditorId;
    private Guid _regularUserId;
    private Guid _clubId;
    private Guid _departmentId;
    private Guid _groupId;

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

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SeedDataAsync();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client?.Dispose();
        _factory?.Dispose();
    }

    [TestMethod]
    public async Task CreateGroup_DepartmentEditor_CreatesGroupBelowDepartment()
    {
        var response = await SendAsync(HttpMethod.Post, "/api/departments", _departmentEditorId, new CreateDepartmentDto
        {
            Name = "Alte Herren",
            ClubId = _clubId,
            ParentDepartmentId = _departmentId,
            Description = "Training: mittwochs 20 Uhr"
        });

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ApiResponse<DepartmentDto>>())!.Data!;
        Assert.IsTrue(created.IsGroup);
        Assert.AreEqual(_departmentId, created.ParentDepartmentId);
        Assert.AreEqual("Handball", created.ParentDepartmentName);
        Assert.AreEqual("Training: mittwochs 20 Uhr", created.Description);

        var parent = await GetDepartmentAsync(_departmentId, _regularUserId);
        CollectionAssert.AreEquivalent(new[] { "Alte Herren", "Bambini" }, parent.Groups.Select(g => g.Name).ToArray());
    }

    [TestMethod]
    public async Task CreateGroup_BelowGroup_ReturnsBadRequest()
    {
        var response = await SendAsync(HttpMethod.Post, "/api/departments", _clubEditorId, new CreateDepartmentDto
        {
            Name = "Zu tief",
            ClubId = _clubId,
            ParentDepartmentId = _groupId
        });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateGroup_GroupEditor_ReturnsForbidden()
    {
        var response = await SendAsync(HttpMethod.Post, "/api/departments", _groupEditorId, new CreateDepartmentDto
        {
            Name = "Neue Gruppe",
            ClubId = _clubId,
            ParentDepartmentId = _departmentId
        });

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task GroupEditor_CanEditOwnGroupButNotDepartment()
    {
        var group = await GetDepartmentAsync(_groupId, _groupEditorId);
        Assert.IsTrue(group.CanEdit);
        Assert.IsFalse(group.CanDelete, "Group editors must not delete their own group.");

        var department = await GetDepartmentAsync(_departmentId, _groupEditorId);
        Assert.IsFalse(department.CanEdit);

        var updateGroup = await SendAsync(HttpMethod.Put, $"/api/departments/{_groupId}", _groupEditorId,
            new UpdateDepartmentDto { Name = "Bambini", Description = "Freitags 16 Uhr" });
        Assert.AreEqual(HttpStatusCode.OK, updateGroup.StatusCode);

        var updateDepartment = await SendAsync(HttpMethod.Put, $"/api/departments/{_departmentId}", _groupEditorId,
            new UpdateDepartmentDto { Name = "Gekapert" });
        Assert.AreNotEqual(HttpStatusCode.OK, updateDepartment.StatusCode);
    }

    [TestMethod]
    public async Task DepartmentEditor_CanEditAndDeleteGroup()
    {
        var group = await GetDepartmentAsync(_groupId, _departmentEditorId);
        Assert.IsTrue(group.CanEdit);
        Assert.IsTrue(group.CanDelete);

        var response = await SendAsync(HttpMethod.Delete, $"/api/departments/{_groupId}", _departmentEditorId);
        Assert.IsTrue(response.IsSuccessStatusCode, $"Unexpected status {response.StatusCode}");
    }

    [TestMethod]
    public async Task GetDepartments_ByClub_ListsGroupsOnlyOnRequestAfterTheirDepartment()
    {
        var withoutGroups = await GetListAsync($"/api/departments?clubId={_clubId}");
        CollectionAssert.AreEqual(new[] { "Handball" }, withoutGroups.Select(d => d.Name).ToArray());

        var withGroups = await GetListAsync($"/api/departments?clubId={_clubId}&includeGroups=true");
        CollectionAssert.AreEqual(new[] { "Handball", "Bambini" }, withGroups.Select(d => d.Name).ToArray());
        Assert.IsTrue(withGroups[1].IsGroup);
    }

    [TestMethod]
    public async Task DepartmentEvents_IncludeGroupEventsWithFullOrganizerPath()
    {
        await SeedEventAsync(_groupId, "Bambini-Spielfest");

        var response = await SendAsync(HttpMethod.Get, $"/api/events/department/{_departmentId}", _regularUserId);
        var events = (await response.Content.ReadFromJsonAsync<ApiResponse<List<EventDto>>>())!.Data!;

        var groupEvent = events.Single(e => e.Title == "Bambini-Spielfest");
        Assert.AreEqual("Bambini", groupEvent.DepartmentName);
        Assert.AreEqual("Handball", groupEvent.ParentDepartmentName);
        Assert.AreEqual("TSV Gruppen", groupEvent.ClubName);
    }

    [TestMethod]
    public async Task DeleteDepartment_ClubEditor_RemovesGroupsAndTheirEvents()
    {
        await SeedEventAsync(_groupId, "Bambini-Spielfest");

        var response = await SendAsync(HttpMethod.Delete, $"/api/departments/{_departmentId}", _clubEditorId);
        Assert.IsTrue(response.IsSuccessStatusCode, $"Unexpected status {response.StatusCode}");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VernetztDbContext>();
        Assert.IsFalse(await db.Departments.AnyAsync(d => d.ClubId == _clubId));
        Assert.IsFalse(await db.Events.AnyAsync(e => e.Title == "Bambini-Spielfest"));
        Assert.IsFalse(await db.ContactAssignments.AnyAsync(a => a.UserId == _groupEditorId));
    }

    [TestMethod]
    public async Task PublicApi_ShowsHierarchyForDepartmentAndGroup()
    {
        var club = await _client.GetFromJsonAsync<PublicClubDto>($"/api/public/v1/clubs/{_clubId}");
        Assert.IsNotNull(club);
        Assert.AreEqual(1, club.Departments.Count, "Groups are listed on their department page, not on the club page.");
        Assert.AreEqual(1, club.Departments[0].GroupCount);

        var department = await _client.GetFromJsonAsync<PublicDepartmentDto>($"/api/public/v1/departments/{_departmentId}");
        Assert.IsNotNull(department);
        Assert.IsNull(department.ParentDepartment);
        Assert.AreEqual("Bambini", department.Groups.Single().Name);

        var group = await _client.GetFromJsonAsync<PublicDepartmentDto>($"/api/public/v1/departments/{_groupId}");
        Assert.IsNotNull(group);
        Assert.AreEqual(_departmentId, group.ParentDepartment?.Id);
        Assert.AreEqual("Handball", group.ParentDepartment?.Name);
        Assert.AreEqual("TSV Gruppen", group.ClubName);
        Assert.AreEqual("Freitags 16 Uhr", group.Description);
    }

    [TestMethod]
    public async Task PublicApi_EmptyGroup_InheritsClubValuesWithSource()
    {
        await UpdateAsync(_clubId, club =>
        {
            club.Street = "Sportplatzweg 1";
            club.PostalCode = "12345";
            club.City = "Musterstadt";
            club.Email = "info@tsv.test";
            club.WebsiteUrl = "https://tsv.test";
            club.SocialMediaLinks = [new SocialMediaLink(SocialMediaPlatform.Instagram, "https://instagram.com/tsv")];
        });

        var group = await GetPublicDepartmentAsync(_groupId);

        Assert.AreEqual("Sportplatzweg 1", group.Street);
        Assert.AreEqual("Musterstadt", group.City);
        Assert.IsTrue(group.HasEmail);
        Assert.AreEqual("https://tsv.test", group.WebsiteUrl);
        Assert.AreEqual("https://instagram.com/tsv", group.SocialMediaLinks.Single().Url);
        foreach (var source in new[] { group.AddressSource, group.EmailSource, group.WebsiteSource, group.SocialMediaSource })
        {
            Assert.AreEqual(PublicInheritedSourceDto.ClubKind, source?.Kind);
            Assert.AreEqual(_clubId, source?.Id);
            Assert.AreEqual("TSV Gruppen", source?.Name);
        }
    }

    [TestMethod]
    public async Task PublicApi_Group_PrefersDepartmentOverClubPerBlock()
    {
        await UpdateAsync(_clubId, club => { club.City = "Vereinsort"; club.Email = "info@tsv.test"; });
        await UpdateDepartmentAsync(_departmentId, d => { d.Street = "Hallenweg 2"; d.City = "Hallenort"; });

        var group = await GetPublicDepartmentAsync(_groupId);

        Assert.AreEqual("Hallenweg 2", group.Street);
        Assert.AreEqual("Hallenort", group.City);
        Assert.AreEqual(PublicInheritedSourceDto.DepartmentKind, group.AddressSource?.Kind);
        Assert.AreEqual(_departmentId, group.AddressSource?.Id);
        Assert.AreEqual(PublicInheritedSourceDto.ClubKind, group.EmailSource?.Kind);
        Assert.IsNull(group.WebsiteUrl);
        Assert.IsNull(group.WebsiteSource);
        Assert.AreEqual(0, group.SocialMediaLinks.Count);
        Assert.IsNull(group.SocialMediaSource);
    }

    [TestMethod]
    public async Task PublicApi_PartialOwnAddress_IsNotMixedWithInheritedValues()
    {
        await UpdateAsync(_clubId, club => { club.Street = "Sportplatzweg 1"; club.PostalCode = "12345"; club.City = "Musterstadt"; });
        await UpdateDepartmentAsync(_departmentId, d => { d.City = "Nur Ort"; d.WebsiteUrl = "https://handball.test"; });

        var department = await GetPublicDepartmentAsync(_departmentId);

        Assert.IsNull(department.Street);
        Assert.IsNull(department.PostalCode);
        Assert.AreEqual("Nur Ort", department.City);
        Assert.IsNull(department.AddressSource, "Own values have no source.");
        Assert.AreEqual("https://handball.test", department.WebsiteUrl);
        Assert.IsNull(department.WebsiteSource);
    }

    [TestMethod]
    public async Task PublicApi_GroupWithoutLogo_FallsBackToDepartmentLogo()
    {
        await UpdateDepartmentAsync(_departmentId, d => d.HasAvatar = true);

        var group = await GetPublicDepartmentAsync(_groupId);
        Assert.IsNotNull(group.LogoUrl);

        var department = await GetPublicDepartmentAsync(_departmentId);
        Assert.IsNotNull(department.Groups.Single().LogoUrl, "Group rows on the department page use the department logo.");
    }

    private async Task<PublicDepartmentDto> GetPublicDepartmentAsync(Guid id) =>
        (await _client.GetFromJsonAsync<PublicDepartmentDto>($"/api/public/v1/departments/{id}"))!;

    private async Task UpdateAsync(Guid clubId, Action<Club> change)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VernetztDbContext>();
        change(await db.Clubs.SingleAsync(c => c.Id == clubId));
        await db.SaveChangesAsync();
    }

    private async Task UpdateDepartmentAsync(Guid departmentId, Action<Department> change)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VernetztDbContext>();
        change(await db.Departments.SingleAsync(d => d.Id == departmentId));
        await db.SaveChangesAsync();
    }

    private async Task<DepartmentDto> GetDepartmentAsync(Guid id, Guid userId)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/departments/{id}", userId);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<DepartmentDto>>())!.Data!;
    }

    private async Task<List<DepartmentDto>> GetListAsync(string url)
    {
        var response = await SendAsync(HttpMethod.Get, url, _regularUserId);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<List<DepartmentDto>>>())!.Data!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, Guid userId, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestHeaderAuthenticationHandler.UserIdHeaderName, userId.ToString());
        if (body != null)
        {
            request.Content = JsonContent.Create(body, body.GetType());
        }

        return await _client.SendAsync(request);
    }

    private async Task SeedEventAsync(Guid departmentId, string title)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VernetztDbContext>();
        var start = DateTime.UtcNow.AddDays(3);
        db.Events.Add(new Event
        {
            Id = Guid.NewGuid(),
            Title = title,
            DepartmentId = departmentId,
            StartUtc = start,
            EndUtc = start.AddHours(2)
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedDataAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VernetztDbContext>();

        _clubEditorId = Guid.NewGuid();
        _departmentEditorId = Guid.NewGuid();
        _groupEditorId = Guid.NewGuid();
        _regularUserId = Guid.NewGuid();
        _clubId = Guid.NewGuid();
        _departmentId = Guid.NewGuid();
        _groupId = Guid.NewGuid();

        User CreateUser(Guid id, string name) => new()
        {
            Id = id,
            FirstName = name,
            LastName = "Test",
            Email = $"{name.ToLowerInvariant()}-{id:N}@test.local",
            EmailVerified = true,
            Claims = []
        };

        db.Users.AddRange(
            CreateUser(_clubEditorId, "Club"),
            CreateUser(_departmentEditorId, "Department"),
            CreateUser(_groupEditorId, "Group"),
            CreateUser(_regularUserId, "Regular"));

        db.Clubs.Add(new Club { Id = _clubId, Name = "TSV Gruppen" });
        db.Departments.Add(new Department { Id = _departmentId, ClubId = _clubId, Name = "Handball" });
        db.Departments.Add(new Department
        {
            Id = _groupId,
            ClubId = _clubId,
            ParentDepartmentId = _departmentId,
            Name = "Bambini",
            Description = "Freitags 16 Uhr"
        });

        ContactAssignment Assign(Guid userId, Guid? clubId, Guid? departmentId) => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ClubId = clubId,
            DepartmentId = departmentId,
            CanEdit = true,
            AssignedAt = DateTime.UtcNow,
            Visibility = VisibilityLevel.Public,
            ConfirmedAt = DateTime.UtcNow
        };

        db.ContactAssignments.AddRange(
            Assign(_clubEditorId, _clubId, null),
            Assign(_departmentEditorId, null, _departmentId),
            Assign(_groupEditorId, null, _groupId));

        await db.SaveChangesAsync();
    }
}
