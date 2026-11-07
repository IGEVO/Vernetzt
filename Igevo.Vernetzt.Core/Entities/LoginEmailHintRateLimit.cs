namespace Igevo.Vernetzt.Core.Entities;

/// <summary>
/// Stores the last time a login hint email was sent for a mailbox hash.
/// Used to enforce configurable cooldown and prevent abuse/spam.
/// </summary>
public class LoginEmailHintRateLimit
{
    public Guid Id { get; set; }

    /// <summary>
    /// SHA-256 hash of normalized mailbox address.
    /// </summary>
    public string EmailHash { get; set; } = string.Empty;

    public DateTime LastSentAt { get; set; }
}
