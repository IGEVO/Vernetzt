namespace Igevo.Vernetzt.Core.Entities;

/// <summary>
/// Records a redeemed login-link token so that each link can be used only once.
/// Only a hash of the token is stored. Entries are removed by the data cleanup job after expiry.
/// </summary>
public class ConsumedLoginToken
{
    public Guid Id { get; set; }

    /// <summary>
    /// SHA-256 hash (hex) of the redeemed token.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    public Guid UserId { get; set; }

    public DateTime ConsumedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Point in time after which the token would have expired anyway; safe to delete afterwards.
    /// </summary>
    public DateTime ExpiresAt { get; set; }
}
