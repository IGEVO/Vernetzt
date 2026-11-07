namespace Igevo.Vernetzt.Core.DTOs.EmailRelay;

/// <summary>
/// Response after creating a new email relay token.
/// </summary>
public class CreateEmailRelayTokenResponse
{
    /// <summary>
    /// The unique ID of the created relay token.
    /// </summary>
    public Guid TokenId { get; set; }

    /// <summary>
    /// The generated email address to send the email to.
    /// </summary>
    public string TokenAddress { get; set; } = string.Empty;

    /// <summary>
    /// When the token expires (UTC).
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Number of recipients the email will be forwarded to.
    /// </summary>
    public int RecipientCount { get; set; }

    /// <summary>
    /// Maximum accepted size of an incoming relay email in bytes (including attachments).
    /// </summary>
    public long MaxMessageSizeBytes { get; set; }
}
