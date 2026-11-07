namespace Igevo.Vernetzt.Core.DTOs.Email;

/// <summary>
/// Response after sending bulk email.
/// </summary>
public class SendBulkEmailResponse
{
    /// <summary>
    /// Whether the email was sent successfully.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Number of recipients the email was sent to.
    /// </summary>
    public int RecipientCount { get; set; }

    /// <summary>
    /// Error message if sending failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
}
