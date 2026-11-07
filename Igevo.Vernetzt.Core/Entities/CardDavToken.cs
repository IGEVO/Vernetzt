using System.ComponentModel.DataAnnotations;

namespace Igevo.Vernetzt.Core.Entities;

/// <summary>
/// Personal app password for read-only CardDAV access to the address book.
/// Only the hash is stored; the raw token is shown once when it is created.
/// </summary>
public class CardDavToken : BaseEntity
{
    /// <summary>
    /// Owner of the access.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// SHA-256 hash of the raw token.
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// Last successful authentication (UTC, updated with a coarse granularity).
    /// </summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// Token revocation flag.
    /// </summary>
    public bool IsRevoked { get; set; }

    /// <summary>
    /// Timestamp when the token was revoked.
    /// </summary>
    public DateTime? RevokedAt { get; set; }

    public virtual User? User { get; set; }
}
