using System.Text.Json;
using Igevo.Vernetzt.Core.DTOs.EmailRelay;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Igevo.Vernetzt.Infrastructure.Services;

/// <summary>
/// Mailbox monitor implementation using MailDev REST API.
/// Used in development to monitor incoming emails without IMAP.
/// </summary>
public class MailDevMailboxMonitorService : IMailboxMonitorService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MailDevMailboxMonitorService> _logger;
    private readonly string _mailDevApiUrl;
    private readonly long _maxMessageSizeBytes;

    public MailDevMailboxMonitorService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<MailDevMailboxMonitorService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _mailDevApiUrl = configuration["EmailRelay:MailDevApiUrl"]
            ?? throw new InvalidOperationException("EmailRelay:MailDevApiUrl is required for MailDev monitor mode");
        _maxMessageSizeBytes = long.Parse(configuration["EmailRelay:MaxMessageSizeBytes"] ?? "20971520");
    }

    public async Task<List<IncomingEmail>> FetchNewEmailsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_mailDevApiUrl}/email", cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var mailDevEmails = JsonSerializer.Deserialize<List<MailDevEmail>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (mailDevEmails == null || mailDevEmails.Count == 0)
            {
                return new List<IncomingEmail>();
            }

            var result = new List<IncomingEmail>();

            foreach (var email in mailDevEmails)
            {
                if (email.Size is > 0 && email.Size > _maxMessageSizeBytes)
                {
                    _logger.LogWarning("MailDev message {EmailId} exceeds size limit: {Size} bytes > {Limit}",
                        email.Id, email.Size, _maxMessageSizeBytes);
                    result.Add(new IncomingEmail
                    {
                        Id = email.Id ?? string.Empty,
                        FromAddress = email.From?.FirstOrDefault()?.Address ?? string.Empty,
                        FromName = email.From?.FirstOrDefault()?.Name ?? string.Empty,
                        ToAddresses = email.To?.Select(t => t.Address ?? string.Empty)
                            .Where(a => !string.IsNullOrEmpty(a)).ToList() ?? new List<string>(),
                        Subject = email.Subject ?? string.Empty,
                        ReceivedAt = email.Date,
                        IsOversized = true,
                        SizeBytes = email.Size.Value
                    });
                    continue;
                }

                var incomingEmail = new IncomingEmail
                {
                    Id = email.Id ?? string.Empty,
                    FromAddress = email.From?.FirstOrDefault()?.Address ?? string.Empty,
                    FromName = email.From?.FirstOrDefault()?.Name ?? string.Empty,
                    ToAddresses = email.To?.Select(t => t.Address ?? string.Empty)
                        .Where(a => !string.IsNullOrEmpty(a)).ToList() ?? new List<string>(),
                    Subject = email.Subject ?? string.Empty,
                    HtmlBody = email.Html ?? string.Empty,
                    TextBody = email.Text ?? string.Empty,
                    ReceivedAt = email.Date ?? DateTime.UtcNow,
                    Attachments = new List<EmailAttachment>()
                };

                // Fetch attachments if any
                if (email.Attachments != null)
                {
                    foreach (var attachment in email.Attachments)
                    {
                        try
                        {
                            var attachmentResponse = await _httpClient.GetAsync(
                                $"{_mailDevApiUrl}/email/{email.Id}/attachment/{attachment.FileName}",
                                cancellationToken);

                            if (attachmentResponse.IsSuccessStatusCode)
                            {
                                var data = await attachmentResponse.Content.ReadAsByteArrayAsync(cancellationToken);
                                incomingEmail.Attachments.Add(new EmailAttachment
                                {
                                    FileName = attachment.FileName ?? "unknown",
                                    ContentType = attachment.ContentType ?? "application/octet-stream",
                                    Content = data
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to fetch attachment {FileName} for email {EmailId}",
                                attachment.FileName, email.Id);
                        }
                    }
                }

                result.Add(incomingEmail);
            }

            _logger.LogDebug("Fetched {Count} emails from MailDev", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch emails from MailDev API at {Url}", _mailDevApiUrl);
            return new List<IncomingEmail>();
        }
    }

    public async Task DeleteEmailAsync(string emailId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.DeleteAsync(
                $"{_mailDevApiUrl}/email/{emailId}",
                cancellationToken);
            response.EnsureSuccessStatusCode();
            _logger.LogDebug("Deleted email {EmailId} from MailDev", emailId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete email {EmailId} from MailDev", emailId);
            throw;
        }
    }

    public async Task<int> DeleteOlderThanAsync(TimeSpan minAge, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_mailDevApiUrl}/email", cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var emails = JsonSerializer.Deserialize<List<MailDevEmail>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new List<MailDevEmail>();

            var cutoff = DateTime.UtcNow - minAge;
            var deleted = 0;
            foreach (var email in emails.Where(e => !string.IsNullOrEmpty(e.Id) && (e.Time ?? e.Date) is { } received && received.ToUniversalTime() < cutoff))
            {
                var deleteResponse = await _httpClient.DeleteAsync($"{_mailDevApiUrl}/email/{email.Id}", cancellationToken);
                if (deleteResponse.IsSuccessStatusCode)
                {
                    deleted++;
                }
            }

            return deleted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean up old messages on MailDev at {Url}", _mailDevApiUrl);
            return 0;
        }
    }

    // MailDev JSON response models
    private class MailDevEmail
    {
        public string? Id { get; set; }
        public List<MailDevAddress>? From { get; set; }
        public List<MailDevAddress>? To { get; set; }
        public string? Subject { get; set; }
        public string? Html { get; set; }
        public string? Text { get; set; }
        public DateTime? Date { get; set; }
        public DateTime? Time { get; set; }
        public long? Size { get; set; }
        public List<MailDevAttachment>? Attachments { get; set; }
    }

    private class MailDevAddress
    {
        public string? Address { get; set; }
        public string? Name { get; set; }
    }

    private class MailDevAttachment
    {
        public string? FileName { get; set; }
        public string? ContentType { get; set; }
    }
}
