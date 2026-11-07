using System.ComponentModel.DataAnnotations;

namespace Igevo.Vernetzt.Core.DTOs.Email;

/// <summary>
/// Request for sending bulk emails to multiple recipients.
/// </summary>
public class SendBulkEmailRequest
{
    /// <summary>
    /// Email subject line.
    /// </summary>
    [Required(ErrorMessage = "Betreff ist erforderlich")]
    [StringLength(200, ErrorMessage = "Betreff darf maximal 200 Zeichen lang sein")]
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// HTML email body content.
    /// </summary>
    [Required(ErrorMessage = "E-Mail-Inhalt ist erforderlich")]
    public string HtmlBody { get; set; } = string.Empty;

    /// <summary>
    /// Plain text fallback (optional).
    /// </summary>
    public string? PlainTextBody { get; set; }

    /// <summary>
    /// List of recipient contact assignment IDs (preferred — server resolves effective emails per assignment).
    /// </summary>
    public List<Guid> RecipientAssignmentIds { get; set; } = new();

    /// <summary>
    /// List of recipient user IDs (compatibility fallback — server resolves emails).
    /// </summary>
    public List<Guid> RecipientUserIds { get; set; } = new();
}
