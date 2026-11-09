using Igevo.Vernetzt.Core.DTOs.EmailRelay;
using Igevo.Vernetzt.Core.Interfaces.Services;
using MailKit.Net.Pop3;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using MimeKit.Utils;

namespace Igevo.Vernetzt.Infrastructure.Services;

/// <summary>
/// Mailbox monitor implementation using POP3 via MailKit.
/// Used with Mailpit in development or POP3-compliant servers in production.
/// </summary>
public class Pop3MailboxMonitorService : IMailboxMonitorService
{
    private readonly ILogger<Pop3MailboxMonitorService> _logger;
    private readonly string _host;
    private readonly int _port;
    private readonly bool _useSsl;
    private readonly string _username;
    private readonly string _password;
    private readonly long _maxMessageSizeBytes;

    public Pop3MailboxMonitorService(
        IConfiguration configuration,
        ILogger<Pop3MailboxMonitorService> logger)
    {
        _logger = logger;
        _host = configuration["EmailRelay:Pop3:Host"]
            ?? throw new InvalidOperationException("EmailRelay:Pop3:Host is required");
        _port = int.Parse(configuration["EmailRelay:Pop3:Port"] ?? "1110");
        _useSsl = bool.Parse(configuration["EmailRelay:Pop3:UseSsl"] ?? "false");
        _username = configuration["EmailRelay:Pop3:Username"] ?? string.Empty;
        _password = configuration["EmailRelay:Pop3:Password"] ?? string.Empty;
        _maxMessageSizeBytes = long.Parse(configuration["EmailRelay:MaxMessageSizeBytes"] ?? "20971520");
    }

    public async Task<List<IncomingEmail>> FetchNewEmailsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<IncomingEmail>();

        using var client = new Pop3Client();

        try
        {
            var secureSocketOptions = _useSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.None;
            await client.ConnectAsync(_host, _port, secureSocketOptions, cancellationToken);
            await client.AuthenticateAsync(_username, _password, cancellationToken);

            if (client.Count == 0)
            {
                await client.DisconnectAsync(true, cancellationToken);
                return result;
            }

            var uids = await client.GetMessageUidsAsync(cancellationToken);

            for (int i = 0; i < client.Count; i++)
            {
                try
                {
                    var uid = uids[i];
                    var size = await client.GetMessageSizeAsync(i, cancellationToken);
                    if (size > _maxMessageSizeBytes)
                    {
                        // Headers only: lets the relay notify the sender; the message itself is removed by the retention cleanup.
                        _logger.LogWarning("POP3 message {Uid} exceeds size limit: {Size} bytes > {Limit}",
                            uid, size, _maxMessageSizeBytes);
                        var headers = await client.GetMessageHeadersAsync(i, cancellationToken);
                        result.Add(MapHeadersToOversizedEmail(headers, uid, size));
                        continue;
                    }

                    var message = await client.GetMessageAsync(i, cancellationToken);
                    var incomingEmail = MapMimeMessageToIncomingEmail(message, uid);
                    result.Add(incomingEmail);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process POP3 message at index {Index}", i);
                }
            }

            await client.DisconnectAsync(true, cancellationToken);
            _logger.LogDebug("Fetched {Count} emails via POP3", result.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch emails via POP3 from {Host}:{Port}", _host, _port);
            if (client.IsConnected)
            {
                await client.DisconnectAsync(false, cancellationToken);
            }
        }

        return result;
    }

    public async Task DeleteEmailAsync(string messageId, CancellationToken cancellationToken = default)
    {
        using var client = new Pop3Client();

        try
        {
            var secureSocketOptions = _useSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.None;
            await client.ConnectAsync(_host, _port, secureSocketOptions, cancellationToken);
            await client.AuthenticateAsync(_username, _password, cancellationToken);

            var uids = await client.GetMessageUidsAsync(cancellationToken);
            var index = -1;
            for (int i = 0; i < uids.Count; i++)
            {
                if (uids[i] == messageId)
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0)
            {
                await client.DeleteMessageAsync(index, cancellationToken);
                _logger.LogDebug("Marked POP3 message {Uid} for deletion", messageId);
            }
            else
            {
                _logger.LogWarning("POP3 message with UID {Uid} not found for deletion", messageId);
            }

            // Disconnect with quit to commit deletions
            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete POP3 message {MessageId}", messageId);
            if (client.IsConnected)
            {
                await client.DisconnectAsync(false, cancellationToken);
            }
            throw;
        }
    }

    public async Task<int> DeleteOlderThanAsync(TimeSpan minAge, CancellationToken cancellationToken = default)
    {
        using var client = new Pop3Client();

        try
        {
            var secureSocketOptions = _useSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.None;
            await client.ConnectAsync(_host, _port, secureSocketOptions, cancellationToken);
            await client.AuthenticateAsync(_username, _password, cancellationToken);

            var cutoff = DateTimeOffset.UtcNow - minAge;
            var deleted = 0;
            for (int i = 0; i < client.Count; i++)
            {
                var headers = await client.GetMessageHeadersAsync(i, cancellationToken);
                // POP3 has no server-side receive date; the Date header is the best available signal.
                if (DateUtils.TryParse(headers[HeaderId.Date], out var date) && date < cutoff)
                {
                    await client.DeleteMessageAsync(i, cancellationToken);
                    deleted++;
                }
            }

            // Disconnect with quit to commit deletions
            await client.DisconnectAsync(true, cancellationToken);
            return deleted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean up old messages on POP3 server");
            if (client.IsConnected)
            {
                await client.DisconnectAsync(false, cancellationToken);
            }
            return 0;
        }
    }

    private static IncomingEmail MapHeadersToOversizedEmail(HeaderList headers, string uid, long size)
    {
        var from = InternetAddressList.TryParse(headers[HeaderId.From] ?? string.Empty, out var fromList)
            ? fromList.Mailboxes.FirstOrDefault()
            : null;
        var to = InternetAddressList.TryParse(headers[HeaderId.To] ?? string.Empty, out var toList)
            ? toList.Mailboxes.Select(m => m.Address).ToList()
            : new List<string>();

        return new IncomingEmail
        {
            Id = uid,
            FromAddress = from?.Address ?? string.Empty,
            FromName = from?.Name ?? string.Empty,
            ToAddresses = to,
            Subject = headers[HeaderId.Subject] ?? string.Empty,
            ReceivedAt = DateUtils.TryParse(headers[HeaderId.Date], out var date) ? date.UtcDateTime : null,
            IsOversized = true,
            SizeBytes = size
        };
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
