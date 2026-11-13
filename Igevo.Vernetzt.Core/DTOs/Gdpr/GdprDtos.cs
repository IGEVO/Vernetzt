using Igevo.Vernetzt.Core.Enums;

namespace Igevo.Vernetzt.Core.DTOs.Gdpr;

/// <summary>
/// Complete user data export for GDPR compliance.
/// </summary>
public class UserDataExport
{
    /// <summary>
    /// Export metadata.
    /// </summary>
    public ExportMetadata Metadata { get; set; } = new();

    /// <summary>
    /// Personal information.
    /// </summary>
    public PersonalData Personal { get; set; } = new();

    /// <summary>
    /// Account information.
    /// </summary>
    public AccountData Account { get; set; } = new();

    /// <summary>
    /// Privacy settings.
    /// </summary>
    public PrivacySettings Privacy { get; set; } = new();

    /// <summary>
    /// Club and department assignments.
    /// </summary>
    public List<AssignmentData> Assignments { get; set; } = new();

    /// <summary>
    /// Audit log entries related to this user.
    /// </summary>
    public List<AuditLogEntry> AuditLog { get; set; } = new();

    /// <summary>
    /// Media files of this user (e.g. profile picture). The files themselves are part of the export archive.
    /// </summary>
    public List<MediaFileData> Media { get; set; } = new();

    /// <summary>
    /// CardDAV address book access (without the password, which is only stored as a hash).
    /// </summary>
    public CardDavAccessData? CardDav { get; set; }
}

/// <summary>
/// Reference to a media file contained in the export archive.
/// </summary>
public class CardDavAccessData
{
    public bool IncludeOrganizations { get; set; }
    public DateTime? AccessCreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}

public class MediaFileData
{
    public string Type { get; set; } = string.Empty; // e.g. "ProfilePicture"
    public string Path { get; set; } = string.Empty; // path inside the export archive
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}

/// <summary>
/// Export metadata.
/// </summary>
public class ExportMetadata
{
    public DateTime ExportedAt { get; set; }
    public string ExportVersion { get; set; } = "1.0";
    public string RequestedBy { get; set; } = string.Empty;
}

/// <summary>
/// Personal data section.
/// </summary>
public class PersonalData
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
}

/// <summary>
/// Account data section.
/// </summary>
public class AccountData
{
    public Guid UserId { get; set; }
    public bool EmailVerified { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public List<string> Claims { get; set; } = new();
}

/// <summary>
/// Privacy settings section.
/// </summary>
public class PrivacySettings
{
    public string EmailVisibility { get; set; } = string.Empty;
    public string PhoneVisibility { get; set; } = string.Empty;
}

/// <summary>
/// Assignment data (club/department contact person).
/// </summary>
public class AssignmentData
{
    public string Type { get; set; } = string.Empty; // "Club" or "Department"
    public Guid EntityId { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public string? PositionName { get; set; }
    public DateTime AssignedAt { get; set; }
    public string? ContextEmail { get; set; }
    public bool ContextEmailVerified { get; set; }
    public DateTime? ContextEmailVerifiedAt { get; set; }
    /// <summary>Who may see the assignment and its role/office address.</summary>
    public string Visibility { get; set; } = string.Empty;

    /// <summary>When the user consented to the assignment; null while pending.</summary>
    public DateTime? ConfirmedAt { get; set; }
}

/// <summary>
/// Audit log entry.
/// </summary>
public class AuditLogEntry
{
    public DateTime Timestamp { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }

    /// <summary>
    /// Who performed the action: "Selbst", "Andere Person" (pseudonymized) or "System".
    /// </summary>
    public string PerformedBy { get; set; } = string.Empty;

    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
}

/// <summary>
/// Request for account deletion.
/// </summary>
public class DeleteAccountRequest
{
    /// <summary>
    /// User must confirm deletion by typing their email.
    /// </summary>
    public string ConfirmEmail { get; set; } = string.Empty;

    /// <summary>
    /// Optional reason for deletion.
    /// </summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Response after account deletion.
/// </summary>
public class DeleteAccountResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime? DeletedAt { get; set; }
    public int AffectedAssignments { get; set; }

    /// <summary>
    /// True if the account was deleted but the user's media files could not be removed from storage.
    /// </summary>
    public bool MediaCleanupFailed { get; set; }
}
