namespace Igevo.Vernetzt.Core.DTOs.EmailRelay;

/// <summary>
/// DTO representing an active email relay token for the UI.
/// </summary>
public class EmailRelayTokenDto
{
    /// <summary>
    /// Unique ID of the relay token.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The generated email address for this relay token.
    /// </summary>
    public string TokenAddress { get; set; } = string.Empty;

    /// <summary>
    /// Number of recipients the email will be forwarded to.
    /// </summary>
    public int RecipientCount { get; set; }

    /// <summary>
    /// When the token was created (UTC).
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the token expires (UTC).
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Whether the token has been used.
    /// </summary>
    public bool IsUsed { get; set; }

    /// <summary>
    /// Optional subject hint for display.
    /// </summary>
    public string? SubjectHint { get; set; }
}
