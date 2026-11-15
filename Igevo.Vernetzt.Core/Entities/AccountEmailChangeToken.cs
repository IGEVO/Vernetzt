namespace Igevo.Vernetzt.Core.Entities;

/// <summary>
/// One-time token that confirms a change of a user's personal (login) email address.
/// The new address only becomes active once the link sent to it has been opened.
/// </summary>
public class AccountEmailChangeToken
{
    public Guid Id { get; set; }

    /// <summary>
    /// The user whose address is being changed.
    /// </summary>
    public Guid UserId { get; set; }

    public virtual User User { get; set; } = null!;

    /// <summary>
    /// SHA-256 hash of the raw token value.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// The requested new address (normalized: trimmed, lower-case).
    /// </summary>
    public string NewEmail { get; set; } = string.Empty;

    /// <summary>
    /// The address the account had when the change was requested.
    /// Prevents confirmation if the address was changed in between.
    /// </summary>
    public string OldEmailSnapshot { get; set; } = string.Empty;

    /// <summary>
    /// The user who requested the change (the user themself or an administrator).
    /// </summary>
    public Guid RequestedByUserId { get; set; }

    public DateTime ExpiresAt { get; set; }
    public bool IsUsed { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
