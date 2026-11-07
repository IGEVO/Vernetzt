using System.Net;
using System.Net.Http.Json;
using Igevo.Vernetzt.Core.DTOs;
using Igevo.Vernetzt.Core.DTOs.Common;
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
public class EventsControllerIntegrationTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    private Guid _adminUserId;
    private Guid _regularUserId;
    private Guid _editorUserId;
    private Guid _departmentEditorUserId;
    private Guid _clubId;
    private Guid _departmentId;

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

        await SeedDataAsync();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client?.Dispose();
        _factory?.Dispose();
    }

    [TestMethod]
    public async Task GetPublicGlobal_WithoutAuthentication_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/events/public/global");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<IEnumerable<EventDto>>>();
        Assert.IsNotNull(payload);
        Assert.IsTrue(payload!.Success);
    }

    [TestMethod]
    public async Task CreateEvent_WithoutAuthentication_ReturnsUnauthorized()
    {
        var dto = BuildCreateEventDto(_clubId, departmentId: null, title: "Unauthorized Event");
        var response = await _client.PostAsJsonAsync("/api/events", dto);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateEvent_RegularUser_ReturnsForbidden()
    {
        var dto = BuildCreateEventDto(_clubId, departmentId: null, title: "No Permission Event");

        using var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/events", _regularUserId, dto);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateEvent_UserWithClubEditAssignment_ReturnsCreated()
    {
        var dto = BuildCreateEventDto(_clubId, departmentId: null, title: "Editor Club Create");

        using var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/events", _editorUserId, dto);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<EventDto>>();
        Assert.IsNotNull(payload);
        Assert.IsTrue(payload!.Success);
        Assert.IsNotNull(payload.Data);
        Assert.AreEqual("Editor Club Create", payload.Data!.Title);
    }

    [TestMethod]
    public async Task CreateDepartmentEvent_RegularUser_ReturnsForbidden()
    {
        var dto = BuildCreateEventDto(clubId: null, departmentId: _departmentId, title: "Department Create Forbidden");

        using var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/events", _regularUserId, dto);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateDepartmentEvent_UserWithDepartmentEditAssignment_ReturnsCreated()
    {
        var dto = BuildCreateEventDto(clubId: null, departmentId: _departmentId, title: "Department Create Direct Editor");

        using var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/events", _departmentEditorUserId, dto);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateDepartmentEvent_UserWithClubEditAssignment_ReturnsCreated_ViaParentClub()
    {
        var dto = BuildCreateEventDto(clubId: null, departmentId: _departmentId, title: "Department Create Club Editor");

        using var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/events", _editorUserId, dto);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }

    [TestMethod]
    public async Task UpdateEvent_WithoutAuthentication_ReturnsUnauthorized()
    {
        var createdEventId = await CreateEventAsAdminAsync(BuildCreateEventDto(_clubId, departmentId: null, "Update Target Unauth"));
        var updateDto = BuildUpdateEventDto("Updated Without Auth");

        var response = await _client.PutAsJsonAsync($"/api/events/{createdEventId}", updateDto);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task UpdateEvent_RegularUserWithoutEditRights_ReturnsNotFound()
    {
        var createdEventId = await CreateEventAsAdminAsync(BuildCreateEventDto(_clubId, departmentId: null, "Update Target Forbidden"));
        var updateDto = BuildUpdateEventDto("Updated Forbidden");

        using var request = CreateAuthenticatedRequest(HttpMethod.Put, $"/api/events/{createdEventId}", _regularUserId, updateDto);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task UpdateEvent_UserWithClubEditAssignment_ReturnsOkAndPersistsChange()
    {
        var createdEventId = await CreateEventAsAdminAsync(BuildCreateEventDto(_clubId, departmentId: null, "Update Target Editor"));
        var updateDto = BuildUpdateEventDto("Updated By Editor");

        using var updateRequest = CreateAuthenticatedRequest(HttpMethod.Put, $"/api/events/{createdEventId}", _editorUserId, updateDto);
        var updateResponse = await _client.SendAsync(updateRequest);

        Assert.AreEqual(HttpStatusCode.OK, updateResponse.StatusCode);

        using var getRequest = CreateAuthenticatedRequest(HttpMethod.Get, $"/api/events/{createdEventId}", _adminUserId);
        var getResponse = await _client.SendAsync(getRequest);
        Assert.AreEqual(HttpStatusCode.OK, getResponse.StatusCode);

        var payload = await getResponse.Content.ReadFromJsonAsync<ApiResponse<EventDto>>();
        Assert.IsNotNull(payload);
        Assert.IsNotNull(payload!.Data);
        Assert.AreEqual("Updated By Editor", payload.Data!.Title);
    }

    [TestMethod]
    public async Task DeleteEvent_WithoutAuthentication_ReturnsUnauthorized()
    {
        var createdEventId = await CreateEventAsAdminAsync(BuildCreateEventDto(_clubId, departmentId: null, "Delete Target Unauth"));

        var response = await _client.DeleteAsync($"/api/events/{createdEventId}");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task DeleteEvent_RegularUserWithoutEditRights_ReturnsNotFound_AndEventStillExists()
    {
        var createdEventId = await CreateEventAsAdminAsync(BuildCreateEventDto(_clubId, departmentId: null, "Delete Target Forbidden"));

        using var deleteRequest = CreateAuthenticatedRequest(HttpMethod.Delete, $"/api/events/{createdEventId}", _regularUserId);
        var deleteResponse = await _client.SendAsync(deleteRequest);
        Assert.AreEqual(HttpStatusCode.NotFound, deleteResponse.StatusCode);

        using var getRequest = CreateAuthenticatedRequest(HttpMethod.Get, $"/api/events/{createdEventId}", _adminUserId);
        var getResponse = await _client.SendAsync(getRequest);
        Assert.AreEqual(HttpStatusCode.OK, getResponse.StatusCode);
    }

    [TestMethod]
    public async Task DeleteEvent_UserWithClubEditAssignment_ReturnsNoContent_AndEventIsDeleted()
    {
        var createdEventId = await CreateEventAsAdminAsync(BuildCreateEventDto(_clubId, departmentId: null, "Delete Target Editor"));

        using var deleteRequest = CreateAuthenticatedRequest(HttpMethod.Delete, $"/api/events/{createdEventId}", _editorUserId);
        var deleteResponse = await _client.SendAsync(deleteRequest);
        Assert.AreEqual(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using var getRequest = CreateAuthenticatedRequest(HttpMethod.Get, $"/api/events/{createdEventId}", _adminUserId);
        var getResponse = await _client.SendAsync(getRequest);
        Assert.AreEqual(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [TestMethod]
    public async Task UpdateDepartmentEvent_RegularUserWithoutEditRights_ReturnsNotFound()
    {
        var createdEventId = await CreateEventAsAdminAsync(BuildCreateEventDto(clubId: null, departmentId: _departmentId, "Department Update Target Forbidden"));
        var updateDto = BuildUpdateEventDto("Department Updated Forbidden");

        using var request = CreateAuthenticatedRequest(HttpMethod.Put, $"/api/events/{createdEventId}", _regularUserId, updateDto);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task UpdateDepartmentEvent_UserWithDepartmentEditAssignment_ReturnsOk()
    {
        var createdEventId = await CreateEventAsAdminAsync(BuildCreateEventDto(clubId: null, departmentId: _departmentId, "Department Update Target Direct Editor"));
        var updateDto = BuildUpdateEventDto("Department Updated Direct Editor");

        using var request = CreateAuthenticatedRequest(HttpMethod.Put, $"/api/events/{createdEventId}", _departmentEditorUserId, updateDto);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task UpdateDepartmentEvent_UserWithClubEditAssignment_ReturnsOk_ViaParentClub()
    {
        var createdEventId = await CreateEventAsAdminAsync(BuildCreateEventDto(clubId: null, departmentId: _departmentId, "Department Update Target Club Editor"));
        var updateDto = BuildUpdateEventDto("Department Updated Club Editor");

        using var request = CreateAuthenticatedRequest(HttpMethod.Put, $"/api/events/{createdEventId}", _editorUserId, updateDto);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task DeleteDepartmentEvent_RegularUserWithoutEditRights_ReturnsNotFound()
    {
        var createdEventId = await CreateEventAsAdminAsync(BuildCreateEventDto(clubId: null, departmentId: _departmentId, "Department Delete Target Forbidden"));

        using var request = CreateAuthenticatedRequest(HttpMethod.Delete, $"/api/events/{createdEventId}", _regularUserId);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task DeleteDepartmentEvent_UserWithDepartmentEditAssignment_ReturnsNoContent()
    {
        var createdEventId = await CreateEventAsAdminAsync(BuildCreateEventDto(clubId: null, departmentId: _departmentId, "Department Delete Target Direct Editor"));

        using var request = CreateAuthenticatedRequest(HttpMethod.Delete, $"/api/events/{createdEventId}", _departmentEditorUserId);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
    }

    [TestMethod]
    public async Task DeleteDepartmentEvent_UserWithClubEditAssignment_ReturnsNoContent_ViaParentClub()
    {
        var createdEventId = await CreateEventAsAdminAsync(BuildCreateEventDto(clubId: null, departmentId: _departmentId, "Department Delete Target Club Editor"));

        using var request = CreateAuthenticatedRequest(HttpMethod.Delete, $"/api/events/{createdEventId}", _editorUserId);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
    }

    [TestMethod]
    public async Task GetByClub_IncludeDepartmentsFilter_Works()
    {
        var clubEventTitle = "Club Event Main";
        var departmentEventTitle = "Department Event Child";

        var createClubEvent = BuildCreateEventDto(_clubId, departmentId: null, clubEventTitle);
        using var createClubRequest = CreateAuthenticatedRequest(HttpMethod.Post, "/api/events", _adminUserId, createClubEvent);
        var createClubResponse = await _client.SendAsync(createClubRequest);
        Assert.AreEqual(HttpStatusCode.Created, createClubResponse.StatusCode);

        var createDepartmentEvent = BuildCreateEventDto(clubId: null, departmentId: _departmentId, departmentEventTitle);
        using var createDepartmentRequest = CreateAuthenticatedRequest(HttpMethod.Post, "/api/events", _adminUserId, createDepartmentEvent);
        var createDepartmentResponse = await _client.SendAsync(createDepartmentRequest);
        Assert.AreEqual(HttpStatusCode.Created, createDepartmentResponse.StatusCode);

        using var listWithoutDepartmentsRequest = CreateAuthenticatedRequest(HttpMethod.Get, $"/api/events/club/{_clubId}?includeDepartments=false", _adminUserId);
        var listWithoutDepartmentsResponse = await _client.SendAsync(listWithoutDepartmentsRequest);
        Assert.AreEqual(HttpStatusCode.OK, listWithoutDepartmentsResponse.StatusCode);

        var withoutDepartmentsPayload = await listWithoutDepartmentsResponse.Content.ReadFromJsonAsync<ApiResponse<IEnumerable<EventDto>>>();
        Assert.IsNotNull(withoutDepartmentsPayload);
        var withoutDepartments = withoutDepartmentsPayload!.Data?.ToList() ?? new List<EventDto>();
        Assert.IsTrue(withoutDepartments.Any(e => e.Title == clubEventTitle));
        Assert.IsFalse(withoutDepartments.Any(e => e.Title == departmentEventTitle));

        using var listWithDepartmentsRequest = CreateAuthenticatedRequest(HttpMethod.Get, $"/api/events/club/{_clubId}?includeDepartments=true", _adminUserId);
        var listWithDepartmentsResponse = await _client.SendAsync(listWithDepartmentsRequest);
        Assert.AreEqual(HttpStatusCode.OK, listWithDepartmentsResponse.StatusCode);

        var withDepartmentsPayload = await listWithDepartmentsResponse.Content.ReadFromJsonAsync<ApiResponse<IEnumerable<EventDto>>>();
        Assert.IsNotNull(withDepartmentsPayload);
        var withDepartments = withDepartmentsPayload!.Data?.ToList() ?? new List<EventDto>();
        Assert.IsTrue(withDepartments.Any(e => e.Title == clubEventTitle));
        Assert.IsTrue(withDepartments.Any(e => e.Title == departmentEventTitle));
    }

    [TestMethod]
    public async Task GetByClubPaged_WithoutAuthentication_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync($"/api/events/club/{_clubId}/paged?page=1&pageSize=5");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task GetByClubPaged_RegularUserWithoutAssignment_SeesAllEvents()
    {
        await CreateEventAsAdminAsync(BuildCreateEventDto(_clubId, departmentId: null, "Öffentlicher Vereinstermin"));

        using var request = CreateAuthenticatedRequest(HttpMethod.Get, $"/api/events/club/{_clubId}/paged?page=1&pageSize=5", _regularUserId);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<EventDto>>>();
        Assert.IsNotNull(payload);
        Assert.IsTrue(payload!.Success);
        Assert.IsNotNull(payload.Data);
        Assert.AreEqual(1, payload.Data!.TotalCount);
        Assert.AreEqual(1, payload.Data.Items.Count());
    }

    [TestMethod]
    public async Task GetByClubPaged_IncludeDepartmentsFilterAndPaging_Works()
    {
        for (var index = 1; index <= 3; index++)
        {
            await CreateEventAsAdminAsync(BuildCreateEventDto(_clubId, departmentId: null, $"Club Paged {index}"));
        }

        for (var index = 1; index <= 2; index++)
        {
            await CreateEventAsAdminAsync(BuildCreateEventDto(clubId: null, departmentId: _departmentId, $"Department Paged {index}"));
        }

        using var withoutDepartmentsRequest = CreateAuthenticatedRequest(HttpMethod.Get, $"/api/events/club/{_clubId}/paged?includeDepartments=false&page=1&pageSize=2", _adminUserId);
        var withoutDepartmentsResponse = await _client.SendAsync(withoutDepartmentsRequest);
        Assert.AreEqual(HttpStatusCode.OK, withoutDepartmentsResponse.StatusCode);

        var withoutDepartmentsPayload = await withoutDepartmentsResponse.Content.ReadFromJsonAsync<ApiResponse<PagedResult<EventDto>>>();
        Assert.IsNotNull(withoutDepartmentsPayload);
        Assert.IsTrue(withoutDepartmentsPayload!.Success);
        Assert.IsNotNull(withoutDepartmentsPayload.Data);
        Assert.AreEqual(3, withoutDepartmentsPayload.Data!.TotalCount);
        Assert.AreEqual(1, withoutDepartmentsPayload.Data.Page);
        Assert.AreEqual(2, withoutDepartmentsPayload.Data.PageSize);
        Assert.AreEqual(2, withoutDepartmentsPayload.Data.Items.Count());
        Assert.IsFalse(withoutDepartmentsPayload.Data.Items.Any(e => e.Title.StartsWith("Department Paged", StringComparison.Ordinal)));

        using var withDepartmentsRequest = CreateAuthenticatedRequest(HttpMethod.Get, $"/api/events/club/{_clubId}/paged?includeDepartments=true&page=2&pageSize=2", _adminUserId);
        var withDepartmentsResponse = await _client.SendAsync(withDepartmentsRequest);
        Assert.AreEqual(HttpStatusCode.OK, withDepartmentsResponse.StatusCode);

        var withDepartmentsPayload = await withDepartmentsResponse.Content.ReadFromJsonAsync<ApiResponse<PagedResult<EventDto>>>();
        Assert.IsNotNull(withDepartmentsPayload);
        Assert.IsTrue(withDepartmentsPayload!.Success);
        Assert.IsNotNull(withDepartmentsPayload.Data);
        Assert.AreEqual(5, withDepartmentsPayload.Data!.TotalCount);
        Assert.AreEqual(2, withDepartmentsPayload.Data.Page);
        Assert.AreEqual(2, withDepartmentsPayload.Data.PageSize);
        Assert.IsTrue(withDepartmentsPayload.Data.Items.Any());
    }

    [TestMethod]
    public async Task GetByDepartmentPaged_WithoutAuthentication_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync($"/api/events/department/{_departmentId}/paged?page=1&pageSize=5");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task GetByDepartmentPaged_RegularUserWithoutAssignment_SeesAllEvents()
    {
        await CreateEventAsAdminAsync(BuildCreateEventDto(clubId: null, departmentId: _departmentId, "Öffentlicher Abteilungstermin"));

        using var request = CreateAuthenticatedRequest(HttpMethod.Get, $"/api/events/department/{_departmentId}/paged?page=1&pageSize=5", _regularUserId);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<EventDto>>>();
        Assert.IsNotNull(payload);
        Assert.IsTrue(payload!.Success);
        Assert.IsNotNull(payload.Data);
        Assert.AreEqual(1, payload.Data!.TotalCount);
        Assert.AreEqual(1, payload.Data.Items.Count());
    }

    [TestMethod]
    public async Task GetByDepartmentPaged_DepartmentEditor_ReturnsPagedData()
    {
        for (var index = 1; index <= 4; index++)
        {
            await CreateEventAsAdminAsync(BuildCreateEventDto(clubId: null, departmentId: _departmentId, $"Department Page Item {index}"));
        }

        using var request = CreateAuthenticatedRequest(HttpMethod.Get, $"/api/events/department/{_departmentId}/paged?page=1&pageSize=3", _departmentEditorUserId);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<EventDto>>>();
        Assert.IsNotNull(payload);
        Assert.IsTrue(payload!.Success);
        Assert.IsNotNull(payload.Data);
        Assert.AreEqual(4, payload.Data!.TotalCount);
        Assert.AreEqual(1, payload.Data.Page);
        Assert.AreEqual(3, payload.Data.PageSize);
        Assert.AreEqual(3, payload.Data.Items.Count());
        Assert.IsTrue(payload.Data.Items.All(e => e.Title.StartsWith("Department Page Item", StringComparison.Ordinal)));
    }

    private static CreateEventDto BuildCreateEventDto(Guid? clubId, Guid? departmentId, string title)
    {
        var start = DateTime.UtcNow.AddDays(3);
        var end = start.AddHours(2);

        return new CreateEventDto
        {
            Title = title,
            Description = "Integration test event",
            Location = "Test Hall",
            StartUtc = start,
            EndUtc = end,
            IsAllDay = false,
            IsHighlight = false,
            ClubId = clubId,
            DepartmentId = departmentId
        };
    }

    private static UpdateEventDto BuildUpdateEventDto(string title)
    {
        var start = DateTime.UtcNow.AddDays(5);
        var end = start.AddHours(1);

        return new UpdateEventDto
        {
            Title = title,
            Description = "Updated description",
            Location = "Updated Hall",
            StartUtc = start,
            EndUtc = end,
            IsAllDay = false,
            IsHighlight = false
        };
    }

    private async Task<Guid> CreateEventAsAdminAsync(CreateEventDto dto)
    {
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/events", _adminUserId, dto);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<EventDto>>();
        Assert.IsNotNull(payload);
        Assert.IsNotNull(payload!.Data);

        return payload.Data!.Id;
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

    private static HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string endpoint, Guid userId)
    {
        var request = new HttpRequestMessage(method, endpoint);
        request.Headers.Add(TestHeaderAuthenticationHandler.UserIdHeaderName, userId.ToString());
        return request;
    }

    private async Task SeedDataAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VernetztDbContext>();

        _adminUserId = Guid.NewGuid();
        _regularUserId = Guid.NewGuid();
        _editorUserId = Guid.NewGuid();
        _departmentEditorUserId = Guid.NewGuid();
        _clubId = Guid.NewGuid();
        _departmentId = Guid.NewGuid();

        db.Users.AddRange(
            new User
            {
                Id = _adminUserId,
                FirstName = "Events",
                LastName = "Admin",
                Email = $"events-admin-{_adminUserId:N}@test.local",
                EmailVerified = true,
                Claims = [UserClaim.SystemAdmin]
            },
            new User
            {
                Id = _regularUserId,
                FirstName = "Events",
                LastName = "User",
                Email = $"events-user-{_regularUserId:N}@test.local",
                EmailVerified = true,
                Claims = []
            },
            new User
            {
                Id = _editorUserId,
                FirstName = "Events",
                LastName = "Editor",
                Email = $"events-editor-{_editorUserId:N}@test.local",
                EmailVerified = true,
                Claims = []
            },
            new User
            {
                Id = _departmentEditorUserId,
                FirstName = "Events",
                LastName = "DepartmentEditor",
                Email = $"events-department-editor-{_departmentEditorUserId:N}@test.local",
                EmailVerified = true,
                Claims = []
            });

        db.Clubs.Add(new Club
        {
            Id = _clubId,
            Name = $"Events Club {_clubId:N}",
            City = "Bochum"
        });

        db.Departments.Add(new Department
        {
            Id = _departmentId,
            ClubId = _clubId,
            Name = $"Events Department {_departmentId:N}"
        });

        db.ContactAssignments.Add(new ContactAssignment
        {
            Id = Guid.NewGuid(),
            UserId = _editorUserId,
            ClubId = _clubId,
            CanEdit = true,
            AssignedAt = DateTime.UtcNow,
            ContextEmailVisibility = VisibilityLevel.AllUsers
        });

        db.ContactAssignments.Add(new ContactAssignment
        {
            Id = Guid.NewGuid(),
            UserId = _departmentEditorUserId,
            DepartmentId = _departmentId,
            CanEdit = true,
            AssignedAt = DateTime.UtcNow,
            ContextEmailVisibility = VisibilityLevel.AllUsers
        });

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }
}
