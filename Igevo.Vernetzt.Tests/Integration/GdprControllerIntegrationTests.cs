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
    private Guid _otherUserId;
    private Guid _adminUserId;

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
    public async Task ExportMyData_IncludesAssignmentsContextEmail_AndSanitizedMailAudit()
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
        Assert.AreEqual("Internal", clubAssignment.Visibility);
        Assert.AreEqual("kontakt+abteilung@test.local", departmentAssignment!.ContextEmail);
        Assert.IsTrue(departmentAssignment.ContextEmailVerified);
        Assert.AreEqual("AllUsers", departmentAssignment.Visibility);

        var relayAudit = export.AuditLog.FirstOrDefault(a => a.Action == AuditAction.EmailRelayForward.ToString());
        Assert.IsNotNull(relayAudit);
        Assert.IsFalse(string.IsNullOrWhiteSpace(relayAudit!.NewValues));
        Assert.IsFalse(relayAudit.NewValues!.Contains("EmailMasked", StringComparison.Ordinal));

        using var json = JsonDocument.Parse(relayAudit.NewValues);
        Assert.AreEqual("Test Versand", json.RootElement.GetProperty("Subject").GetString());
        Assert.AreEqual(2, json.RootElement.GetProperty("RecipientCount").GetInt32());
        Assert.IsFalse(json.RootElement.TryGetProperty("Recipients", out _));
        Assert.AreEqual("Selbst", relayAudit.PerformedBy);
    }

    [TestMethod]
    public async Task ExportMyData_OmitsThirdPartyDataFromOwnActions_AndIncludesActionsOnOwnAccount()
    {
        using var request = CreateAuthenticatedRequest("/api/gdpr/export", _userId);
        var response = await _client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var export = (await response.Content.ReadFromJsonAsync<ApiResponse<UserDataExport>>())!.Data!;

        // The user edited another account: only metadata, no snapshot of the other person.
        var otherUserEdit = export.AuditLog.Single(a => a.EntityId == _otherUserId);
        Assert.AreEqual("Selbst", otherUserEdit.PerformedBy);
        Assert.IsNull(otherUserEdit.OldValues);
        Assert.IsNull(otherUserEdit.NewValues);

        // An administrator edited this user's account: included, actor pseudonymized, actor IP removed.
        var editByAdmin = export.AuditLog.Single(a => a.EntityId == _userId && a.Action == AuditAction.Update.ToString());
        Assert.AreEqual("Andere Person", editByAdmin.PerformedBy);
        Assert.IsNotNull(editByAdmin.NewValues);
        Assert.IsTrue(editByAdmin.NewValues!.Contains("0170 1234567", StringComparison.Ordinal));
        Assert.IsFalse(editByAdmin.NewValues.Contains("IpAddress", StringComparison.Ordinal));
        Assert.IsFalse(JsonSerializer.Serialize(export.AuditLog).Contains(_adminUserId.ToString(), StringComparison.OrdinalIgnoreCase));

        Assert.IsFalse(JsonSerializer.Serialize(export).Contains("fremd@test.local", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task DownloadMyData_ReturnsZipArchive_WithSanitizedMailAudit()
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
        Assert.AreEqual("Internal", clubAssignment.GetProperty("visibility").GetString());

        var audit = json.RootElement.GetProperty("auditLog").EnumerateArray()
            .FirstOrDefault(a => string.Equals(a.GetProperty("action").GetString(), AuditAction.EmailRelayForward.ToString(), StringComparison.Ordinal));
        Assert.AreNotEqual(default, audit);

        var recipients = audit.GetProperty("newValues").GetString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(recipients));
        Assert.IsFalse(recipients!.Contains("EmailMasked", StringComparison.Ordinal));

        Assert.IsFalse(recipients.Contains("Kontakt Eins", StringComparison.Ordinal));

        using var nested = JsonDocument.Parse(recipients);
        Assert.AreEqual(2, nested.RootElement.GetProperty("RecipientCount").GetInt32());
        Assert.IsFalse(nested.RootElement.TryGetProperty("Recipients", out _));
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
            ConfirmedAt = DateTime.UtcNow,
            Id = clubAssignmentId,
            UserId = _userId,
            ClubId = _clubId,
            ContactPositionId = positionId,
            ContextEmail = "kontakt+club@test.local",
            ContextEmailNormalized = "kontakt+club@test.local",
            ContextEmailVerified = true,
            ContextEmailVerifiedAt = DateTime.UtcNow.AddDays(-2),
            Visibility = VisibilityLevel.Internal,
            AssignedAt = DateTime.UtcNow.AddDays(-20)
        };

        var departmentAssignment = new ContactAssignment
        {
            ConfirmedAt = DateTime.UtcNow,
            Id = departmentAssignmentId,
            UserId = _userId,
            DepartmentId = _departmentId,
            ContactPositionId = positionId,
            ContextEmail = "kontakt+abteilung@test.local",
            ContextEmailNormalized = "kontakt+abteilung@test.local",
            ContextEmailVerified = true,
            ContextEmailVerifiedAt = DateTime.UtcNow.AddDays(-1),
            Visibility = VisibilityLevel.AllUsers,
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

        _otherUserId = Guid.NewGuid();
        _adminUserId = Guid.NewGuid();

        // The exporting user edited another person's account: the entry carries that person's data.
        var editOfOtherUser = new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            Action = AuditAction.Update,
            EntityType = nameof(User),
            EntityId = _otherUserId,
            Timestamp = DateTime.UtcNow.AddMinutes(-10),
            OldValues = JsonSerializer.Serialize(new { FirstName = "Fremd", Email = "fremd@test.local", PhoneNumber = "0171 999999" }),
            NewValues = JsonSerializer.Serialize(new { FirstName = "Fremd", Email = "fremd@test.local", PhoneNumber = "0171 000000" })
        };

        // An administrator edited the exporting user's account.
        var editByAdmin = new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = _adminUserId,
            Action = AuditAction.Update,
            EntityType = nameof(User),
            EntityId = _userId,
            Timestamp = DateTime.UtcNow.AddMinutes(-5),
            NewValues = JsonSerializer.Serialize(new { PhoneNumber = "0170 1234567", IpAddress = "10.0.0.7", UpdatedById = _adminUserId })
        };

        db.Users.Add(user);
        db.Clubs.Add(club);
        db.Departments.Add(department);
        db.ContactPositions.Add(position);
        db.ContactAssignments.AddRange(clubAssignment, departmentAssignment);
        db.AuditLogs.AddRange(audit, editOfOtherUser, editByAdmin);

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Assert.IsNotNull(await db.Users.FirstOrDefaultAsync(u => u.Id == _userId));
    }
}
