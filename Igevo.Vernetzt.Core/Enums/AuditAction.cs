namespace Igevo.Vernetzt.Core.Enums;

/// <summary>
/// Defines the type of audit action performed.
/// </summary>
public enum AuditAction
{
    /// <summary>
    /// Entity was created.
    /// </summary>
    Create = 0,

    /// <summary>
    /// Entity was updated.
    /// </summary>
    Update = 1,

    /// <summary>
    /// Entity was deleted.
    /// </summary>
    Delete = 2,

    /// <summary>
    /// User logged in.
    /// </summary>
    Login = 3,

    /// <summary>
    /// User logged out.
    /// </summary>
    Logout = 4,

    /// <summary>
    /// User accessed/viewed data.
    /// </summary>
    View = 5,

    /// <summary>
    /// Email was forwarded via relay token.
    /// </summary>
    EmailRelayForward = 6,

    /// <summary>
    /// Bulk email was sent to multiple recipients via BCC.
    /// </summary>
    EmailBulkSend = 7,

    /// <summary>
    /// Login attempt failed (e.g., unknown email address).
    /// </summary>
    LoginAttemptFailed = 8,

    /// <summary>
    /// Incoming relay email was rejected (e.g., invalid/expired token).
    /// </summary>
    EmailRelayRejected = 9
}
