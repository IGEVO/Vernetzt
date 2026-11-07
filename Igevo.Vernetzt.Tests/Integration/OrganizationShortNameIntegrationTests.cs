using System.Net;
using System.Net.Http.Json;
using Igevo.Vernetzt.Core.DTOs;
using Igevo.Vernetzt.Core.DTOs.Clubs;
using Igevo.Vernetzt.Core.DTOs.Departments;
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
public class OrganizationShortNameIntegrationTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private Guid _adminId;

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
        _client.DefaultRequestHeaders.Add(TestHeaderAuthenticationHandler.UserIdHeaderName, (_adminId = Guid.NewGuid()).ToString());
        _client.DefaultRequestHeaders.Add(TestHeaderAuthenticationHandler.RolesHeaderName, nameof(UserClaim.SystemAdmin));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VernetztDbContext>();
        db.Users.Add(new User
        {
            Id = _adminId, FirstName = "Short", LastName = "Admin",
            Email = $"short-admin-{_adminId:N}@test.local", EmailVerified = true, Claims = [UserClaim.SystemAdmin]
        });
        await db.SaveChangesAsync();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [TestMethod]
    public async Task Club_ShortName_IsTrimmedStoredAndClearable()
    {
        var created = await ReadAsync<ClubDto>(await _client.PostAsJsonAsync("/api/clubs",
            new CreateClubDto { Name = $"Turnverein Musterstadt {Guid.NewGuid():N}", ShortName = "  TVM " }), HttpStatusCode.Created);
        Assert.AreEqual("TVM", created.ShortName);

        var update = new UpdateClubDto { Name = created.Name, ShortName = "  " };
        await ReadAsync<ClubDto>(await _client.PutAsJsonAsync($"/api/clubs/{created.Id}", update), HttpStatusCode.OK);

        var loaded = await ReadAsync<ClubDto>(await _client.GetAsync($"/api/clubs/{created.Id}"), HttpStatusCode.OK);
        Assert.IsNull(loaded.ShortName);
    }

    [TestMethod]
    public async Task Club_ShortName_TooLong_IsRejected()
    {
        var response = await _client.PostAsJsonAsync("/api/clubs",
            new CreateClubDto { Name = $"Verein {Guid.NewGuid():N}", ShortName = new string('X', 21) });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Department_ShortName_IsStored()
    {
        var club = await ReadAsync<ClubDto>(await _client.PostAsJsonAsync("/api/clubs",
            new CreateClubDto { Name = $"Verein {Guid.NewGuid():N}" }), HttpStatusCode.Created);

        var department = await ReadAsync<DepartmentDto>(await _client.PostAsJsonAsync("/api/departments",
            new CreateDepartmentDto { Name = "Fußball", ShortName = "FB", ClubId = club.Id }), HttpStatusCode.Created);
        Assert.AreEqual("FB", department.ShortName);

        await ReadAsync<DepartmentDto>(await _client.PutAsJsonAsync($"/api/departments/{department.Id}",
            new UpdateDepartmentDto { Name = "Fußball", ShortName = "Fußb." }), HttpStatusCode.OK);
        var loaded = await ReadAsync<DepartmentDto>(await _client.GetAsync($"/api/departments/{department.Id}"), HttpStatusCode.OK);
        Assert.AreEqual("Fußb.", loaded.ShortName);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(expected, response.StatusCode, body);
        var payload = System.Text.Json.JsonSerializer.Deserialize<ApiResponse<T>>(body,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
            {
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            });
        return payload!.Data!;
    }
}
