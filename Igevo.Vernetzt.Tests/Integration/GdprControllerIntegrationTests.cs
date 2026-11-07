using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Igevo.Vernetzt.Core.DTOs;
using Igevo.Vernetzt.Core.DTOs.Gdpr;
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

[TestClass]
public class GdprControllerIntegrationTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private Guid _userId;
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

        await SeedExportDataAsync();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client?.Dispose();
        _factory?.Dispose();
    }

    [TestMethod]
    public async Task ExportMyData_IncludesAssignmentsContextEmail_AndSanitizedAuditRecipients()
    {
        using var request = CreateAuthenticatedRequest("/api/gdpr/export", _userId);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var api = await response.Content.ReadFromJsonAsync<ApiResponse<UserDataExport>>();
        Assert.IsNotNull(api);
        Assert.IsTrue(api!.Success);
        Assert.IsNotNull(api.Data);

        var export = api.Data!;
        var clubAssignment = export.Assignments.FirstOrDefault(a => a.EntityId == _clubId);
        var departmentAssignment = export.Assignments.FirstOrDefault(a => a.EntityId == _departmentId);

        Assert.IsNotNull(clubAssignment);
        Assert.IsNotNull(departmentAssignment);
        Assert.AreEqual("kontakt+club@test.local", clubAssignment!.ContextEmail);
        Assert.IsTrue(clubAssignment.ContextEmailVerified);
        Assert.AreEqual("Internal", clubAssignment.ContextEmailVisibility);
        Assert.AreEqual("kontakt+abteilung@test.local", departmentAssignment!.ContextEmail);
        Assert.IsTrue(departmentAssignment.ContextEmailVerified);
        Assert.AreEqual("AllUsers", departmentAssignment.ContextEmailVisibility);

        var relayAudit = export.AuditLog.FirstOrDefault(a => a.Action == AuditAction.EmailRelayForward.ToString());
        Assert.IsNotNull(relayAudit);
        Assert.IsFalse(string.IsNullOrWhiteSpace(relayAudit!.NewValues));
        Assert.IsFalse(relayAudit.NewValues!.Contains("EmailMasked", StringComparison.Ordinal));

        using var json = JsonDocument.Parse(relayAudit.NewValues);
        var recipients = json.RootElement.GetProperty("Recipients").EnumerateArray().ToList();
        Assert.AreEqual(2, recipients.Count);
        Assert.AreEqual("Kontakt Eins (Beispielverein)", recipients[0].GetProperty("Name").GetString());
        Assert.AreEqual("Kontakt Zwei (Beispielverein)", recipients[1].GetProperty("Name").GetString());
        Assert.IsFalse(recipients[0].TryGetProperty("EmailMasked", out _));
        Assert.IsFalse(recipients[1].TryGetProperty("EmailMasked", out _));
    }

    [TestMethod]
    public async Task DownloadMyData_ReturnsZipArchive_WithSanitizedAuditRecipients()
    {
        using var request = CreateAuthenticatedRequest("/api/gdpr/export/download", _userId);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(response.Content.Headers.ContentType?.MediaType?.Equals("application/zip", StringComparison.OrdinalIgnoreCase) == true);
        Assert.IsTrue(response.Content.Headers.ContentDisposition?.FileName?.Contains("datenexport_", StringComparison.OrdinalIgnoreCase) == true);

        using var zip = new System.IO.Compression.ZipArchive(await response.Content.ReadAsStreamAsync(), System.IO.Compression.ZipArchiveMode.Read);
        var jsonEntry = zip.GetEntry("datenexport.json");
        Assert.IsNotNull(jsonEntry);
        using var jsonReader = new StreamReader(jsonEntry.Open());
        var jsonText = await jsonReader.ReadToEndAsync();
        using var json = JsonDocument.Parse(jsonText);

        var assignments = json.RootElement.GetProperty("assignments").EnumerateArray().ToList();
        var clubAssignment = assignments.FirstOrDefault(a => a.GetProperty("entityId").GetGuid() == _clubId);
        Assert.AreNotEqual(default, clubAssignment);
        Assert.AreEqual("kontakt+club@test.local", clubAssignment.GetProperty("contextEmail").GetString());
        Assert.AreEqual("Internal", clubAssignment.GetProperty("contextEmailVisibility").GetString());

        var audit = json.RootElement.GetProperty("auditLog").EnumerateArray()
            .FirstOrDefault(a => string.Equals(a.GetProperty("action").GetString(), AuditAction.EmailRelayForward.ToString(), StringComparison.Ordinal));
        Assert.AreNotEqual(default, audit);

        var recipients = audit.GetProperty("newValues").GetString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(recipients));
        Assert.IsFalse(recipients!.Contains("EmailMasked", StringComparison.Ordinal));

        using var nested = JsonDocument.Parse(recipients);
        var recipientArray = nested.RootElement.GetProperty("Recipients").EnumerateArray().ToList();
        Assert.AreEqual(2, recipientArray.Count);
        Assert.IsTrue(recipientArray.All(r => r.TryGetProperty("Name", out _)));
        Assert.IsTrue(recipientArray.All(r => !r.TryGetProperty("EmailMasked", out _)));
    }

    private HttpRequestMessage CreateAuthenticatedRequest(string endpoint, Guid userId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Add(TestHeaderAuthenticationHandler.UserIdHeaderName, userId.ToString());
        return request;
    }

    private async Task SeedExportDataAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VernetztDbContext>();

        _userId = Guid.NewGuid();
        _clubId = Guid.NewGuid();
        _departmentId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var clubAssignmentId = Guid.NewGuid();
        var departmentAssignmentId = Guid.NewGuid();

        var user = new User
        {
            Id = _userId,
            FirstName = "Gdpr",
            LastName = "Tester",
            Email = $"gdpr-user-{_userId:N}@test.local",
            EmailVerified = true,
            Claims = []
        };

        var club = new Club
        {
            Id = _clubId,
            Name = $"GDPR Club {_clubId:N}",
            City = "Teststadt"
        };

        var department = new Department
        {
            Id = _departmentId,
            ClubId = _clubId,
            Name = $"GDPR Abteilung {_departmentId:N}"
        };

        var position = new ContactPosition
        {
            Id = positionId,
            Name = "Vorstand"
        };

        var clubAssignment = new ContactAssignment
        {
            Id = clubAssignmentId,
            UserId = _userId,
            ClubId = _clubId,
            ContactPositionId = positionId,
            ContextEmail = "kontakt+club@test.local",
            ContextEmailNormalized = "kontakt+club@test.local",
            ContextEmailVerified = true,
            ContextEmailVerifiedAt = DateTime.UtcNow.AddDays(-2),
            ContextEmailVisibility = VisibilityLevel.Internal,
            AssignedAt = DateTime.UtcNow.AddDays(-20)
        };

        var departmentAssignment = new ContactAssignment
        {
            Id = departmentAssignmentId,
            UserId = _userId,
            DepartmentId = _departmentId,
            ContactPositionId = positionId,
            ContextEmail = "kontakt+abteilung@test.local",
            ContextEmailNormalized = "kontakt+abteilung@test.local",
            ContextEmailVerified = true,
            ContextEmailVerifiedAt = DateTime.UtcNow.AddDays(-1),
            ContextEmailVisibility = VisibilityLevel.AllUsers,
            AssignedAt = DateTime.UtcNow.AddDays(-10)
        };

        var audit = new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            Action = AuditAction.EmailRelayForward,
            EntityType = "EmailRelayToken",
            EntityId = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow.AddMinutes(-15),
            NewValues = JsonSerializer.Serialize(new
            {
                Subject = "Test Versand",
                RecipientCount = 2,
                Recipients = new[]
                {
                    new { Name = "Kontakt Eins (Beispielverein)", EmailMasked = "k***@verein.de" },
                    new { Name = "Kontakt Zwei (Beispielverein)", EmailMasked = "z***@verein.de" }
                }
            })
        };

        db.Users.Add(user);
        db.Clubs.Add(club);
        db.Departments.Add(department);
        db.ContactPositions.Add(position);
        db.ContactAssignments.AddRange(clubAssignment, departmentAssignment);
        db.AuditLogs.Add(audit);

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Assert.IsNotNull(await db.Users.FirstOrDefaultAsync(u => u.Id == _userId));
    }
}
