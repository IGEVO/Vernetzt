namespace Igevo.Vernetzt.Core.Entities;

/// <summary>
/// Represents a temporary, single-use email relay token.
/// Users create a token to receive a unique email address,
/// send an email to that address from their own client,
/// and the app forwards it to the selected recipients via BCC.
/// </summary>
public class EmailRelayToken : BaseEntity
{
    /// <summary>
    /// The generated email address for this relay token.
    /// Either a full catch-all address (e.g., token@relay.example.com)
    /// or a plus-addressed variant (e.g., relay+token@example.com).
    /// </summary>
    public string TokenAddress { get; set; } = string.Empty;

    /// <summary>
    /// List of recipient email addresses (stored as JSON in database).
    /// </summary>
    public List<string> RecipientEmails { get; set; } = new();

    /// <summary>
    /// Email address of the user who created this token.
    /// Used for sending the summary reply after processing.
    /// </summary>
    public string SenderEmail { get; set; } = string.Empty;

    /// <summary>
    /// User ID of the person who created this relay token.
    /// </summary>
    public Guid CreatedByUserId { get; set; }

    /// <summary>
    /// When the relay token expires (UTC).
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Whether the token has been used (an email was successfully forwarded).
    /// Single-use: once true, no further emails are accepted.
    /// </summary>
    public bool IsUsed { get; set; }

    /// <summary>
    /// When the token was used (email was forwarded).
    /// </summary>
    public DateTime? UsedAt { get; set; }

    /// <summary>
    /// Optional subject hint for display in the UI.
    /// </summary>
    public string? SubjectHint { get; set; }

    /// <summary>
    /// Number of recipients the email was forwarded to (set after processing).
    /// </summary>
    public int? ForwardedToCount { get; set; }
}
