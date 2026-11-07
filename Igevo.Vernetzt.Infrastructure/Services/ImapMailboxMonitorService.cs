using Igevo.Vernetzt.Core.DTOs.EmailRelay;
using Igevo.Vernetzt.Core.Interfaces.Services;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace Igevo.Vernetzt.Infrastructure.Services;

/// <summary>
/// Mailbox monitor implementation using IMAP via MailKit.
/// Used in production to monitor incoming emails.
/// </summary>
public class ImapMailboxMonitorService : IMailboxMonitorService
{
    private readonly ILogger<ImapMailboxMonitorService> _logger;
    private readonly string _imapHost;
    private readonly int _imapPort;
    private readonly bool _useSsl;
    private readonly string _username;
    private readonly string _password;
    private readonly long _maxMessageSizeBytes;
    private readonly int _maxMessagesPerPoll;

    public ImapMailboxMonitorService(
        IConfiguration configuration,
        ILogger<ImapMailboxMonitorService> logger)
    {
        _logger = logger;
        _imapHost = configuration["EmailRelay:Imap:Host"]
            ?? throw new InvalidOperationException("EmailRelay:Imap:Host is required");
        _imapPort = int.Parse(configuration["EmailRelay:Imap:Port"] ?? "993");
        _useSsl = bool.Parse(configuration["EmailRelay:Imap:UseSsl"] ?? "true");
        _username = configuration["EmailRelay:Imap:Username"]
            ?? throw new InvalidOperationException("EmailRelay:Imap:Username is required");
        _password = configuration["EmailRelay:Imap:Password"]
            ?? throw new InvalidOperationException("EmailRelay:Imap:Password is required");
        _maxMessageSizeBytes = long.Parse(configuration["EmailRelay:MaxMessageSizeBytes"] ?? "20971520");
        _maxMessagesPerPoll = int.Parse(configuration["EmailRelay:MaxMessagesPerPoll"] ?? "25");
    }

    public async Task<List<IncomingEmail>> FetchNewEmailsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<IncomingEmail>();

        using var client = new ImapClient();

        try
        {
            var secureSocketOptions = _useSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;
            await client.ConnectAsync(_imapHost, _imapPort, secureSocketOptions, cancellationToken);
            await client.AuthenticateAsync(_username, _password, cancellationToken);

            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

            if (inbox.Count == 0)
            {
                return result;
            }

            var messages = await inbox.FetchAsync(0, -1, MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.Size, cancellationToken);

            // Bound memory per poll: download at most _maxMessagesPerPoll messages. Oversized ones are
            // reported as metadata only and never downloaded.
            foreach (var summary in messages)
            {
                if (summary.Size is null)
                {
                    _logger.LogWarning("Skipping IMAP message UID {Uid}: size unknown", summary.UniqueId);
                    continue;
                }

                if (summary.Size > _maxMessageSizeBytes)
                {
                    // Metadata only: lets the relay notify the sender; the message itself is removed by the retention cleanup.
                    _logger.LogWarning("IMAP message UID {Uid} exceeds size limit: {Size} bytes > {Limit}",
                        summary.UniqueId, summary.Size, _maxMessageSizeBytes);
                    result.Add(new IncomingEmail
                    {
                        Id = summary.UniqueId.ToString(),
                        FromAddress = summary.Envelope?.From.Mailboxes.FirstOrDefault()?.Address ?? string.Empty,
                        FromName = summary.Envelope?.From.Mailboxes.FirstOrDefault()?.Name ?? string.Empty,
                        ToAddresses = summary.Envelope?.To.Mailboxes.Select(m => m.Address).ToList() ?? new List<string>(),
                        Subject = summary.Envelope?.Subject ?? string.Empty,
                        ReceivedAt = summary.Envelope?.Date?.UtcDateTime,
                        IsOversized = true,
                        SizeBytes = summary.Size.Value
                    });
                    continue;
                }

                if (result.Count(e => !e.IsOversized) >= _maxMessagesPerPoll)
                {
                    break;
                }

                try
                {
                    var message = await inbox.GetMessageAsync(summary.UniqueId, cancellationToken);
                    var incomingEmail = MapMimeMessageToIncomingEmail(message, summary.UniqueId.ToString());
                    result.Add(incomingEmail);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process IMAP message UID {Uid}", summary.UniqueId);
                }
            }

            await client.DisconnectAsync(true, cancellationToken);
            _logger.LogDebug("Fetched {Count} emails via IMAP", result.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch emails via IMAP from {Host}:{Port}", _imapHost, _imapPort);
            if (client.IsConnected)
            {
                await client.DisconnectAsync(false, cancellationToken);
            }
        }

        return result;
    }

    public async Task DeleteEmailAsync(string emailId, CancellationToken cancellationToken = default)
    {
        using var client = new ImapClient();

        try
        {
            var secureSocketOptions = _useSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;
            await client.ConnectAsync(_imapHost, _imapPort, secureSocketOptions, cancellationToken);
            await client.AuthenticateAsync(_username, _password, cancellationToken);

            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

            if (uint.TryParse(emailId, out var uid))
            {
                var uniqueId = new UniqueId(uid);
                await inbox.AddFlagsAsync(uniqueId, MessageFlags.Deleted, true, cancellationToken);
                await inbox.ExpungeAsync(cancellationToken);
                _logger.LogDebug("Deleted email UID {Uid} from IMAP server", emailId);
            }

            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete email {EmailId} from IMAP server", emailId);
            if (client.IsConnected)
            {
                await client.DisconnectAsync(false, cancellationToken);
            }
            throw;
        }
    }

    public async Task<int> DeleteOlderThanAsync(TimeSpan minAge, CancellationToken cancellationToken = default)
    {
        using var client = new ImapClient();

        try
        {
            var secureSocketOptions = _useSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;
            await client.ConnectAsync(_imapHost, _imapPort, secureSocketOptions, cancellationToken);
            await client.AuthenticateAsync(_username, _password, cancellationToken);

            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

            var deleted = 0;
            if (inbox.Count > 0)
            {
                var cutoff = DateTimeOffset.UtcNow - minAge;
                var summaries = await inbox.FetchAsync(0, -1, MessageSummaryItems.UniqueId | MessageSummaryItems.InternalDate, cancellationToken);
                var expired = summaries
                    .Where(s => s.InternalDate is { } received && received < cutoff)
                    .Select(s => s.UniqueId)
                    .ToList();

                if (expired.Count > 0)
                {
                    await inbox.AddFlagsAsync(expired, MessageFlags.Deleted, true, cancellationToken);
                    await inbox.ExpungeAsync(cancellationToken);
                    deleted = expired.Count;
                }
            }

            await client.DisconnectAsync(true, cancellationToken);
            return deleted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean up old messages on IMAP server");
            if (client.IsConnected)
            {
                await client.DisconnectAsync(false, cancellationToken);
            }
            return 0;
        }
    }

    private static IncomingEmail MapMimeMessageToIncomingEmail(MimeMessage message, string uid)
    {
        var incomingEmail = new IncomingEmail
        {
            Id = uid,
            FromAddress = message.From.Mailboxes.FirstOrDefault()?.Address ?? string.Empty,
            FromName = message.From.Mailboxes.FirstOrDefault()?.Name ?? string.Empty,
            ToAddresses = message.To.Mailboxes.Select(m => m.Address).ToList(),
            Subject = message.Subject ?? string.Empty,
            HtmlBody = message.HtmlBody ?? string.Empty,
            TextBody = message.TextBody ?? string.Empty,
            ReceivedAt = message.Date.UtcDateTime,
            Attachments = new List<EmailAttachment>()
        };

        // Extract attachments
        foreach (var attachment in message.Attachments)
        {
            if (attachment is MimePart mimePart && mimePart.Content != null)
            {
                using var stream = new MemoryStream();
                mimePart.Content.DecodeTo(stream);
                incomingEmail.Attachments.Add(new EmailAttachment
                {
                    FileName = mimePart.FileName ?? "attachment",
                    ContentType = mimePart.ContentType.MimeType,
                    Content = stream.ToArray()
                });
            }
        }

        return incomingEmail;
    }
}
