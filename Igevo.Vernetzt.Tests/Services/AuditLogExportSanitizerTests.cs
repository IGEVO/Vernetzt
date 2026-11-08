using System.Text.Json;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Infrastructure.Services;

namespace Igevo.Vernetzt.Tests.Services;

[TestClass]
public class AuditLogExportSanitizerTests
{
    private static readonly Guid SubjectId = Guid.NewGuid();
    private static readonly Guid AssignmentId = Guid.NewGuid();
    private static readonly HashSet<Guid> SubjectEntityIds = [SubjectId, AssignmentId];

    private static AuditLog Log(AuditAction action, Guid actor, Guid entityId, string? newValues, string? oldValues = null) => new()
    {
        Id = Guid.NewGuid(),
        Action = action,
        UserId = actor,
        EntityType = "Test",
        EntityId = entityId,
        Timestamp = DateTime.UtcNow,
        OldValues = oldValues,
        NewValues = newValues
    };

    [TestMethod]
    public void OwnActionOnOtherUser_ExportsMetadataOnly()
    {
        var snapshot = JsonSerializer.Serialize(new { Email = "fremd@test.local", PhoneNumber = "0171 1" });
        var entry = AuditLogExportSanitizer.ToExportEntry(
            Log(AuditAction.Update, SubjectId, Guid.NewGuid(), snapshot, snapshot), SubjectId, SubjectEntityIds);

        Assert.AreEqual(AuditLogExportSanitizer.PerformedBySelf, entry.PerformedBy);
        Assert.AreEqual("Update", entry.Action);
        Assert.IsNull(entry.OldValues);
        Assert.IsNull(entry.NewValues);
    }

    [TestMethod]
    public void OwnActionOnOwnAccount_ExportsValuesUnchanged()
    {
        var values = JsonSerializer.Serialize(new { Method = "Passkey", IpAddress = "10.0.0.1" });
        var entry = AuditLogExportSanitizer.ToExportEntry(
            Log(AuditAction.Login, SubjectId, SubjectId, values), SubjectId, SubjectEntityIds);

        Assert.AreEqual(values, entry.NewValues);
    }

    [TestMethod]
    public void OtherPersonsActionOnSubject_PseudonymizesActorAndRemovesActorFields()
    {
        var adminId = Guid.NewGuid();
        var values = JsonSerializer.Serialize(new
        {
            PhoneNumber = "0170 1",
            UpdatedById = adminId,
            Nested = new { IpAddress = "10.0.0.9" }
        });
        var entry = AuditLogExportSanitizer.ToExportEntry(
            Log(AuditAction.Update, adminId, SubjectId, values), SubjectId, SubjectEntityIds);

        Assert.AreEqual(AuditLogExportSanitizer.PerformedByOther, entry.PerformedBy);
        Assert.IsNotNull(entry.NewValues);
        Assert.IsTrue(entry.NewValues!.Contains("0170 1", StringComparison.Ordinal));
        Assert.IsFalse(entry.NewValues.Contains(adminId.ToString(), StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(entry.NewValues.Contains("10.0.0.9", StringComparison.Ordinal));
    }

    [TestMethod]
    public void SystemActionOnSubjectAssignment_KeepsPlainTextNotes()
    {
        var entry = AuditLogExportSanitizer.ToExportEntry(
            Log(AuditAction.Update, Guid.Empty, AssignmentId, "Context email verified."), SubjectId, SubjectEntityIds);

        Assert.AreEqual(AuditLogExportSanitizer.PerformedBySystem, entry.PerformedBy);
        Assert.AreEqual("Context email verified.", entry.NewValues);
    }

    [TestMethod]
    public void RelayForward_KeepsOnlySubjectAndRecipientCount()
    {
        var values = JsonSerializer.Serialize(new
        {
            Subject = "Einladung",
            IncomingFrom = new { Name = "Max Fremd", EmailMasked = "m***@firma.de" },
            RelayOwnerUserId = SubjectId,
            RelaySenderEmailMasked = "s***@test.local",
            RecipientCount = 2,
            Recipients = new[] { new { Name = "Kontakt Eins", EmailMasked = "k***@verein.de" } }
        });
        var entry = AuditLogExportSanitizer.ToExportEntry(
            Log(AuditAction.EmailRelayForward, SubjectId, Guid.NewGuid(), values), SubjectId, SubjectEntityIds);

        using var doc = JsonDocument.Parse(entry.NewValues!);
        var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        CollectionAssert.AreEquivalent(new[] { "Subject", "RecipientCount" }, keys);
    }

    [TestMethod]
    public void RelayRejected_DropsThirdPartySenderAddress()
    {
        var values = JsonSerializer.Serialize(new
        {
            Reason = "TokenExpired",
            IncomingEmailId = "abc",
            FromAddressMasked = "f***@extern.de",
            ToAddressesMasked = new[] { "r***@relay.local" },
            Subject = "Hallo"
        });
        var entry = AuditLogExportSanitizer.ToExportEntry(
            Log(AuditAction.EmailRelayRejected, SubjectId, Guid.NewGuid(), values), SubjectId, SubjectEntityIds);

        Assert.IsFalse(entry.NewValues!.Contains("Masked", StringComparison.Ordinal));
        Assert.IsFalse(entry.NewValues.Contains("extern.de", StringComparison.Ordinal));
        using var doc = JsonDocument.Parse(entry.NewValues);
        Assert.AreEqual("TokenExpired", doc.RootElement.GetProperty("Reason").GetString());
        Assert.AreEqual("Hallo", doc.RootElement.GetProperty("Subject").GetString());
    }

    [TestMethod]
    public void BulkEmail_DropsSenderAndReplyTo()
    {
        var values = JsonSerializer.Serialize(new
        {
            Subject = "Rundmail",
            SentBy = new { UserId = SubjectId, Name = "Ich", EmailMasked = "i***@test.local" },
            ReplyTo = new { Name = "Andere", EmailMasked = "a***@test.local" },
            RecipientCount = 5,
            Recipients = new[] { new { EmailMasked = "x***@test.local" } }
        });
        // BulkEmail entries use the sender's user ID as entity ID, so they count as "about the subject".
        var entry = AuditLogExportSanitizer.ToExportEntry(
            Log(AuditAction.EmailBulkSend, SubjectId, SubjectId, values), SubjectId, SubjectEntityIds);

        using var doc = JsonDocument.Parse(entry.NewValues!);
        var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        CollectionAssert.AreEquivalent(new[] { "Subject", "RecipientCount" }, keys);
    }

    [TestMethod]
    public void MailEntryWithUnstructuredValues_ExportsNothing()
    {
        var entry = AuditLogExportSanitizer.ToExportEntry(
            Log(AuditAction.EmailRelayForward, SubjectId, Guid.NewGuid(), "sent to max@extern.de"), SubjectId, SubjectEntityIds);

        Assert.IsNull(entry.NewValues);
    }
}
