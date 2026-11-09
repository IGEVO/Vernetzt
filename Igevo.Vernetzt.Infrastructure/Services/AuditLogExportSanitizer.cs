using System.Text.Json;
using System.Text.Json.Nodes;
using Igevo.Vernetzt.Core.DTOs.Gdpr;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;

namespace Igevo.Vernetzt.Infrastructure.Services;

/// <summary>
/// Builds the audit log section of a GDPR data export.
/// Audit entries can hold personal data of third parties, for example the full user snapshot written
/// when a manager edits another account, or names and addresses from relay and bulk mails. Values are
/// therefore exported by whitelist: only where they describe the data subject, everything else is
/// reduced to its metadata (timestamp, action, entity).
/// </summary>
public static class AuditLogExportSanitizer
{
    public const string PerformedBySelf = "Selbst";
    public const string PerformedByOther = "Andere Person";
    public const string PerformedBySystem = "System";

    private static readonly HashSet<AuditAction> MailActions =
    [
        AuditAction.EmailBulkSend,
        AuditAction.EmailRelayForward,
        AuditAction.EmailRelayRejected
    ];

    // Mail entries also carry names and (masked) addresses of senders and recipients.
    // Only these fields describe the user's own action without revealing anyone else.
    private static readonly string[] MailFields =
    [
        "Subject",
        "RecipientCount",
        "Reason",
        "SizeBytes",
        "MaxMessageSizeBytes"
    ];

    // Fields identifying whoever performed an action (entity snapshots carry the editor's ID via
    // BaseEntity); removed when that was someone else, so the actor stays pseudonymized.
    private static readonly string[] ActorFields = ["IpAddress", "CreatedById", "UpdatedById"];

    /// <summary>
    /// Converts an audit log entry into its export representation for the given data subject.
    /// </summary>
    /// <param name="log">The audit log entry.</param>
    /// <param name="subjectUserId">The user whose data is exported.</param>
    /// <param name="subjectEntityIds">IDs of entities that represent the subject (the user itself and their contact assignments).</param>
    public static AuditLogEntry ToExportEntry(AuditLog log, Guid subjectUserId, IReadOnlySet<Guid> subjectEntityIds)
    {
        var entry = new AuditLogEntry
        {
            Timestamp = log.Timestamp,
            Action = log.Action.ToString(),
            EntityType = log.EntityType,
            EntityId = log.EntityId,
            PerformedBy = DescribeActor(log.UserId, subjectUserId)
        };

        if (MailActions.Contains(log.Action))
        {
            entry.OldValues = KeepOnly(log.OldValues, MailFields);
            entry.NewValues = KeepOnly(log.NewValues, MailFields);
        }
        else if (subjectEntityIds.Contains(log.EntityId))
        {
            var performedBySubject = log.UserId == subjectUserId;
            entry.OldValues = performedBySubject ? log.OldValues : Remove(log.OldValues, ActorFields);
            entry.NewValues = performedBySubject ? log.NewValues : Remove(log.NewValues, ActorFields);
        }
        // Otherwise the entry is about someone else's data (another user, a club, ...): metadata only.

        return entry;
    }

    private static string DescribeActor(Guid actorUserId, Guid subjectUserId)
    {
        if (actorUserId == subjectUserId)
        {
            return PerformedBySelf;
        }

        return actorUserId == Guid.Empty ? PerformedBySystem : PerformedByOther;
    }

    private static string? KeepOnly(string? rawValues, IReadOnlyCollection<string> allowedFields)
    {
        if (string.IsNullOrWhiteSpace(rawValues))
        {
            return null;
        }

        if (TryParseObject(rawValues) is not { } source)
        {
            // Unstructured content cannot be checked field by field, so it is not exported.
            return null;
        }

        var result = new JsonObject();
        foreach (var (key, value) in source)
        {
            if (allowedFields.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                result[key] = value?.DeepClone();
            }
        }

        return result.Count == 0 ? null : result.ToJsonString();
    }

    private static string? Remove(string? rawValues, IReadOnlyCollection<string> fields)
    {
        if (string.IsNullOrWhiteSpace(rawValues))
        {
            return rawValues;
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(rawValues);
        }
        catch (JsonException)
        {
            // Plain-text notes written by the application itself.
            return rawValues;
        }

        if (node == null)
        {
            return rawValues;
        }

        RemoveRecursive(node, fields);
        return node.ToJsonString();
    }

    private static void RemoveRecursive(JsonNode node, IReadOnlyCollection<string> fields)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (fields.Contains(key, StringComparer.OrdinalIgnoreCase))
                    {
                        obj.Remove(key);
                    }
                    else if (obj[key] is { } child)
                    {
                        RemoveRecursive(child, fields);
                    }
                }
                break;
            case JsonArray array:
                foreach (var child in array)
                {
                    if (child != null)
                    {
                        RemoveRecursive(child, fields);
                    }
                }
                break;
        }
    }

    private static JsonObject? TryParseObject(string rawValues)
    {
        try
        {
            return JsonNode.Parse(rawValues) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
