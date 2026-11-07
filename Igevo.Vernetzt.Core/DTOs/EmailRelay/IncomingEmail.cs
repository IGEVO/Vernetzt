namespace Igevo.Vernetzt.Core.DTOs.EmailRelay;

/// <summary>
/// Represents an email fetched from the monitored mailbox.
/// Used as an intermediate model between the mailbox monitor and the relay service.
/// </summary>
public class IncomingEmail
{
    /// <summary>
    /// Provider-specific email ID (MailDev ID or IMAP UID).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Sender email address (From header).
    /// </summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>
    /// Sender display name (From header).
    /// </summary>
    public string? FromName { get; set; }

    /// <summary>
    /// Recipient email addresses (To header).
    /// </summary>
    public List<string> ToAddresses { get; set; } = new();

    /// <summary>
    /// Email subject line.
    /// </summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// HTML body content.
    /// </summary>
    public string? HtmlBody { get; set; }

    /// <summary>
    /// Plain text body content.
    /// </summary>
    public string? TextBody { get; set; }

    /// <summary>
    /// Email attachments.
    /// </summary>
    public List<EmailAttachment> Attachments { get; set; } = new();

    /// <summary>
    /// When the email was received.
    /// </summary>
    public DateTime? ReceivedAt { get; set; }

    /// <summary>
    /// True if the message exceeds the configured maximum size. Only the envelope metadata
    /// (sender, recipients, subject, size) is populated; bodies and attachments are not downloaded.
    /// </summary>
    public bool IsOversized { get; set; }

    /// <summary>
    /// Raw message size in bytes as reported by the mail server (0 if unknown).
    /// </summary>
    public long SizeBytes { get; set; }
}

/// <summary>
/// Represents an email attachment with its content.
/// </summary>
public class EmailAttachment
{
    /// <summary>
    /// Original filename of the attachment.
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// MIME content type (e.g., "application/pdf", "image/png").
    /// </summary>
    public string ContentType { get; set; } = "application/octet-stream";

    /// <summary>
    /// Attachment content as byte array.
    /// </summary>
    public byte[] Content { get; set; } = Array.Empty<byte>();
}
