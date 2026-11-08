using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text.Json;
using Igevo.Vernetzt.Core.DTOs.EmailRelay;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Igevo.Vernetzt.Infrastructure.Services;

/// <summary>
/// Service for managing email relay tokens and processing incoming relay emails.
/// Coordinates between mailbox monitoring, token lifecycle, and email forwarding.
/// </summary>
public class EmailRelayService : IEmailRelayService
{
    private sealed record RelayRecipientDelivery(string Email, string DisplayName, bool IsValidEmail);

    private const int MinMailboxRetentionHours = 1;
    private static readonly TimeSpan NotifiedEntryLifetime = TimeSpan.FromDays(7);

    // Oversized messages stay in the mailbox until the retention cleanup removes them. This remembers which ones
    // the sender was already told about so repeated polls do not send duplicate notifications.
    // Static because the service itself is scoped; an app restart may re-notify once.
    private static readonly ConcurrentDictionary<string, DateTime> NotifiedOversizedIds = new();

    private readonly IEmailRelayTokenRepository _tokenRepository;
    private readonly IMailboxMonitorService _mailboxMonitor;
    private readonly IEmailService _emailService;
    private readonly IUserRepository _userRepository;
    private readonly IRecipientResolutionService _recipientResolutionService;
    private readonly IAuditLogRepository _auditRepository;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailRelayService> _logger;
    private readonly VernetztMetrics? _metrics;

    private readonly string _baseAddress;
    private readonly string _addressingMode;
    private readonly string? _forwardingFromAddress;
    private readonly int _tokenValidityMinutes;
    private readonly bool _addInfoBanner;
    private readonly bool _sendSummaryReply;
    private readonly int _maxActiveTokensPerUser;
    private readonly string _baseUrl;
    private readonly long _maxMessageSizeBytes;
    private readonly TimeSpan _mailboxRetention;

    public EmailRelayService(
        IEmailRelayTokenRepository tokenRepository,
        IMailboxMonitorService mailboxMonitor,
        IEmailService emailService,
        IUserRepository userRepository,
        IRecipientResolutionService recipientResolutionService,
        IAuditLogRepository auditRepository,
        IConfiguration configuration,
        ILogger<EmailRelayService> logger,
        VernetztMetrics? metrics = null)
    {
        _tokenRepository = tokenRepository;
        _mailboxMonitor = mailboxMonitor;
        _emailService = emailService;
        _userRepository = userRepository;
        _recipientResolutionService = recipientResolutionService;
        _auditRepository = auditRepository;
        _configuration = configuration;
        _logger = logger;
        _metrics = metrics;

        _baseAddress = configuration["EmailRelay:BaseAddress"]
            ?? throw new InvalidOperationException("EmailRelay:BaseAddress is required");
        _addressingMode = configuration["EmailRelay:AddressingMode"] ?? "PlusAddressing";
        var configuredForwardingFrom = configuration["EmailRelay:ForwardingFromAddress"];
        _forwardingFromAddress = string.IsNullOrWhiteSpace(configuredForwardingFrom)
            ? ResolveForwardingFromAddress(_baseAddress)
            : ResolveForwardingFromAddress(configuredForwardingFrom);
        _tokenValidityMinutes = int.Parse(configuration["EmailRelay:TokenValidityMinutes"] ?? "30");
        _addInfoBanner = bool.Parse(configuration["EmailRelay:AddInfoBanner"] ?? "true");
        _sendSummaryReply = bool.Parse(configuration["EmailRelay:SendSummaryReply"] ?? "true");
        _maxActiveTokensPerUser = int.Parse(configuration["EmailRelay:MaxActiveTokensPerUser"] ?? "10");
        _baseUrl = configuration["App:BaseUrl"]?.TrimEnd('/') ?? "";
        _maxMessageSizeBytes = long.Parse(configuration["EmailRelay:MaxMessageSizeBytes"] ?? "20971520");
        var retentionHours = int.Parse(configuration["EmailRelay:MailboxRetentionHours"] ?? "24");
        _mailboxRetention = TimeSpan.FromHours(Math.Max(retentionHours, MinMailboxRetentionHours));

        if (_forwardingFromAddress == null)
        {
            if (string.IsNullOrWhiteSpace(configuredForwardingFrom))
            {
                _logger.LogWarning(
                    "EmailRelay:BaseAddress is not a valid sender address. Forwarding falls back to Email:From:Address.");
            }
            else
            {
                _logger.LogWarning(
                    "EmailRelay:ForwardingFromAddress is not a valid sender address. Forwarding falls back to Email:From:Address.");
            }
        }
    }

    public async Task<CreateEmailRelayTokenResponse> CreateTokenAsync(
        CreateEmailRelayTokenRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // Check rate limit
        var activeCount = await _tokenRepository.CountActiveTokensByUserAsync(userId, cancellationToken);
        if (activeCount >= _maxActiveTokensPerUser)
        {
            throw new InvalidOperationException(
                $"Maximale Anzahl aktiver Tokens ({_maxActiveTokensPerUser}) erreicht. Bitte warte, bis bestehende Tokens ablaufen, oder lösche nicht mehr benötigte Tokens.");
        }

        // Get the sender user's email
        var senderUser = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (senderUser == null)
        {
            throw new InvalidOperationException("User not found");
        }

        var resolution = await _recipientResolutionService.ResolveForSendAsync(
            userId,
            request.RecipientAssignmentIds,
            request.RecipientUserIds,
            cancellationToken);
        if (resolution.Forbidden)
        {
            throw new UnauthorizedAccessException("Mindestens ein Empfänger ist nicht zugänglich.");
        }

        var recipientEmails = resolution.RecipientEmails;

        if (recipientEmails.Count == 0)
        {
            throw new InvalidOperationException("Keine gültigen Empfänger gefunden.");
        }

        // Generate cryptographically random token
        var tokenHex = GenerateSecureToken();
        var tokenAddress = GenerateTokenAddress(tokenHex);

        var token = new EmailRelayToken
        {
            Id = Guid.NewGuid(),
            TokenAddress = tokenAddress,
            RecipientEmails = recipientEmails,
            SenderEmail = senderUser.Email,
            CreatedByUserId = userId,
            ExpiresAt = DateTime.UtcNow.AddMinutes(_tokenValidityMinutes),
            IsUsed = false,
            SubjectHint = request.SubjectHint,
            CreatedAt = DateTime.UtcNow
        };

        await _tokenRepository.AddAsync(token, cancellationToken);

        _logger.LogInformation(
            "Email relay token created: TokenId={TokenId}, Sender={Sender}, Recipients={Count}, ExpiresAt={ExpiresAt}",
            token.Id, MaskAddress(senderUser.Email), recipientEmails.Count, token.ExpiresAt);

        return new CreateEmailRelayTokenResponse
        {
            TokenId = token.Id,
            TokenAddress = tokenAddress,
            ExpiresAt = token.ExpiresAt,
            RecipientCount = recipientEmails.Count,
            MaxMessageSizeBytes = _maxMessageSizeBytes
        };
    }

    public async Task ProcessIncomingEmailsAsync(CancellationToken cancellationToken = default)
    {
        var emails = await _mailboxMonitor.FetchNewEmailsAsync(cancellationToken);

        if (emails.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Processing {Count} incoming emails", emails.Count);

        foreach (var email in emails)
        {
            try
            {
                await ProcessSingleEmailAsync(email, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process incoming email {EmailId} from {From}",
                    email.Id, MaskAddress(email.FromAddress));
                _metrics?.RelayEmailProcessed(VernetztMetrics.RelayOutcomes.Error);
            }
        }
    }

    public async Task CleanupMailboxAsync(CancellationToken cancellationToken = default)
    {
        var deleted = await _mailboxMonitor.DeleteOlderThanAsync(_mailboxRetention, cancellationToken);
        if (deleted > 0)
        {
            _logger.LogInformation(
                "Mailbox cleanup removed {Count} messages older than {Hours}h", deleted, _mailboxRetention.TotalHours);
        }
    }

    public async Task CleanupExpiredTokensAsync(CancellationToken cancellationToken = default)
    {
        var expiredTokens = await _tokenRepository.GetExpiredTokensAsync(cancellationToken);

        foreach (var token in expiredTokens)
        {
            await _tokenRepository.DeleteAsync(token, cancellationToken);
        }

        if (expiredTokens.Count > 0)
        {
            _logger.LogInformation("Cleaned up {Count} expired relay tokens", expiredTokens.Count);
        }
    }

    public async Task<List<EmailRelayTokenDto>> GetActiveTokensAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var tokens = await _tokenRepository.GetActiveTokensByUserAsync(userId, cancellationToken);

        return tokens.Select(t => new EmailRelayTokenDto
        {
            Id = t.Id,
            TokenAddress = t.TokenAddress,
            RecipientCount = t.RecipientEmails.Count,
            CreatedAt = t.CreatedAt,
            ExpiresAt = t.ExpiresAt,
            IsUsed = t.IsUsed,
            SubjectHint = t.SubjectHint
        }).ToList();
    }

    public async Task<bool> CancelTokenAsync(
        Guid tokenId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var token = await _tokenRepository.GetByIdAsync(tokenId, cancellationToken);

        if (token == null)
        {
            return false;
        }

        if (token.CreatedByUserId != userId)
        {
            throw new UnauthorizedAccessException("Cannot cancel another user's relay token");
        }

        token.IsUsed = true; // Mark as cancelled by reusing the IsUsed flag
        token.UsedAt = DateTime.UtcNow;
        await _tokenRepository.UpdateAsync(token, cancellationToken);

        _logger.LogInformation("Email relay token cancelled: TokenId={TokenId} by User={UserId}", tokenId, userId);
        return true;
    }

    private async Task ProcessSingleEmailAsync(IncomingEmail email, CancellationToken cancellationToken)
    {
        // Email addresses are deliberately masked in logs (DSGVO/GDPR: only first char + domain visible).
        _logger.LogDebug(
            "Processing email {EmailId}: From={From}, To=[{ToAddresses}], Subject={Subject}",
            email.Id, MaskAddress(email.FromAddress),
            string.Join(", ", email.ToAddresses.Select(MaskAddress)),
            email.Subject);

        if (email.IsOversized)
        {
            await ProcessOversizedEmailAsync(email, cancellationToken);
            return;
        }

        // Try to match the email to a token address
        string? matchedAddress = null;
        foreach (var toAddress in email.ToAddresses)
        {
            matchedAddress = toAddress;
            var activeToken = await _tokenRepository.GetActiveTokenByAddressAsync(toAddress, cancellationToken);

            if (activeToken != null)
            {
                // Valid, active token found — forward the email
                await ForwardEmailAsync(email, activeToken, cancellationToken);
                await _mailboxMonitor.DeleteEmailAsync(email.Id, cancellationToken);
                return;
            }
        }

        // Check if any address matches an expired or used token
        foreach (var toAddress in email.ToAddresses)
        {
            var anyToken = await _tokenRepository.GetTokenByAddressAsync(toAddress, cancellationToken);
            if (anyToken != null)
            {
                // Token exists but is expired or used — send failure notification
                var reason = anyToken.IsUsed
                    ? "Der Token wurde bereits verwendet (Einmalverwendung)."
                    : "Der Token ist abgelaufen.";

                // Notify the token owner (a known app user), never the From header: it is forgeable and
                // would turn the relay into a backscatter source.
                await SendFailureNotificationAsync(anyToken.SenderEmail, reason, cancellationToken, originalSubject: email.Subject);
                await _mailboxMonitor.DeleteEmailAsync(email.Id, cancellationToken);

                await TryLogAuditAsync(
                    entityType: "EmailRelay",
                    entityId: anyToken.Id,
                    action: AuditAction.EmailRelayRejected,
                    userId: anyToken.CreatedByUserId,
                    newValues: JsonSerializer.Serialize(new
                    {
                        Reason = anyToken.IsUsed ? "TokenAlreadyUsed" : "TokenExpired",
                        IncomingEmailId = email.Id,
                        FromAddressMasked = MaskAddress(email.FromAddress),
                        ToAddressesMasked = email.ToAddresses.Select(MaskAddress).ToList(),
                        Subject = email.Subject
                    }),
                    notes: $"Relay email rejected: {(anyToken.IsUsed ? "token already used" : "token expired")}",
                    cancellationToken: cancellationToken);

                _logger.LogWarning(
                    "Email discarded — token expired/used: EmailId={EmailId}, From={From}, Token={Token}, Subject={Subject}, Reason={Reason}",
                    email.Id, MaskAddress(email.FromAddress), MaskAddress(toAddress),
                    email.Subject, anyToken.IsUsed ? "bereits verwendet" : "abgelaufen");
                _metrics?.RelayEmailProcessed(VernetztMetrics.RelayOutcomes.DiscardedTokenExpired);
                return;
            }
        }

        // No matching token at all — log and silently delete (catch-all cleanup)
        _logger.LogWarning(
            "Email discarded — no matching relay token: EmailId={EmailId}, From={From}, To=[{ToAddresses}], Subject={Subject}",
            email.Id, MaskAddress(email.FromAddress),
            string.Join(", ", email.ToAddresses.Select(MaskAddress)),
            email.Subject);
        _metrics?.RelayEmailProcessed(VernetztMetrics.RelayOutcomes.DiscardedNoToken);

        await TryLogAuditAsync(
            entityType: "EmailRelay",
            entityId: Guid.Empty,
            action: AuditAction.EmailRelayRejected,
            userId: Guid.Empty,
            newValues: JsonSerializer.Serialize(new
            {
                Reason = "NoMatchingToken",
                IncomingEmailId = email.Id,
                FromAddressMasked = MaskAddress(email.FromAddress),
                ToAddressesMasked = email.ToAddresses.Select(MaskAddress).ToList(),
                Subject = email.Subject
            }),
            notes: "Relay email rejected: no matching token",
            cancellationToken: cancellationToken);

        await _mailboxMonitor.DeleteEmailAsync(email.Id, cancellationToken);
    }

    private async Task ProcessOversizedEmailAsync(IncomingEmail email, CancellationToken cancellationToken)
    {
        EmailRelayToken? activeToken = null;
        foreach (var toAddress in email.ToAddresses)
        {
            activeToken = await _tokenRepository.GetActiveTokenByAddressAsync(toAddress, cancellationToken);
            if (activeToken != null)
            {
                break;
            }
        }

        if (activeToken == null)
        {
            // Nobody to notify: drop it right away instead of re-evaluating it on every poll.
            _logger.LogWarning(
                "Oversized email discarded — no active relay token: EmailId={EmailId}, From={From}, Size={Size}",
                email.Id, MaskAddress(email.FromAddress), email.SizeBytes);
            _metrics?.RelayEmailProcessed(VernetztMetrics.RelayOutcomes.RejectedTooLarge);
            await _mailboxMonitor.DeleteEmailAsync(email.Id, cancellationToken);
            return;
        }

        // Keep the message for debugging; the retention cleanup removes it later.
        if (!NotifiedOversizedIds.TryAdd(email.Id, DateTime.UtcNow))
        {
            return;
        }
        PruneNotifiedOversizedIds();

        // The notification goes to the token owner (a known app user), never to the From header,
        // which is forgeable and would turn the relay into a backscatter source.
        var reason = $"Deine E-Mail ist mit {FormatSize(email.SizeBytes)} zu groß. " +
                     $"Die maximale Größe beträgt {FormatSize(_maxMessageSizeBytes)} inklusive Anhängen " +
                     "(Anhänge werden beim Versand rund ein Drittel größer, erlaubt sind daher etwa " +
                     $"{FormatSize(_maxMessageSizeBytes * 3 / 4)} Anhänge).";
        const string hint = "Die Adresse ist weiterhin gültig. Bitte sende eine kleinere E-Mail (z. B. mit weniger oder kleineren Anhängen) an dieselbe Adresse.";
        await SendFailureNotificationAsync(activeToken.SenderEmail, reason, cancellationToken, hint, email.Subject);

        await TryLogAuditAsync(
            entityType: "EmailRelay",
            entityId: activeToken.Id,
            action: AuditAction.EmailRelayRejected,
            userId: activeToken.CreatedByUserId,
            newValues: JsonSerializer.Serialize(new
            {
                Reason = "MessageTooLarge",
                IncomingEmailId = email.Id,
                SizeBytes = email.SizeBytes,
                MaxMessageSizeBytes = _maxMessageSizeBytes,
                FromAddressMasked = MaskAddress(email.FromAddress),
                Subject = email.Subject
            }),
            notes: "Relay email rejected: message too large",
            cancellationToken: cancellationToken);

        _logger.LogWarning(
            "Email rejected — too large: EmailId={EmailId}, TokenId={TokenId}, From={From}, Size={Size}, Limit={Limit}",
            email.Id, activeToken.Id, MaskAddress(email.FromAddress), email.SizeBytes, _maxMessageSizeBytes);
        _metrics?.RelayEmailProcessed(VernetztMetrics.RelayOutcomes.RejectedTooLarge);
    }

    private static void PruneNotifiedOversizedIds()
    {
        var cutoff = DateTime.UtcNow - NotifiedEntryLifetime;
        foreach (var entry in NotifiedOversizedIds.Where(e => e.Value < cutoff))
        {
            NotifiedOversizedIds.TryRemove(entry.Key, out _);
        }
    }

    private static string FormatSize(long bytes) =>
        string.Create(CultureInfo.GetCultureInfo("de-DE"), $"{bytes / 1024d / 1024d:0.#} MB");

    private async Task ForwardEmailAsync(IncomingEmail email, EmailRelayToken token, CancellationToken cancellationToken)
    {
        var originalHtmlBody = email.HtmlBody;
        var originalTextBody = email.TextBody;

        var recipientDeliveries = await ResolveRecipientDeliveriesAsync(token.RecipientEmails, cancellationToken);
        var validRecipientEmails = recipientDeliveries
            .Where(r => r.IsValidEmail)
            .Select(r => r.Email)
            .ToList();

        var htmlBody = email.HtmlBody ?? string.Empty;
        var textBody = email.TextBody ?? string.Empty;

        if (_addInfoBanner)
        {
            var infoBanner = GenerateInfoBanner(email.FromAddress, email.FromName ?? string.Empty, validRecipientEmails.Count);
            var textBanner = GeneratePlainTextInfoBanner(email.FromAddress, email.FromName ?? string.Empty, validRecipientEmails.Count);

            if (!string.IsNullOrEmpty(htmlBody))
            {
                htmlBody = infoBanner + htmlBody;
            }
            else if (!string.IsNullOrEmpty(textBody))
            {
                // Plain-text-only email: wrap in minimal HTML with info banner
                htmlBody = infoBanner + $"<div style='font-family: Arial, sans-serif; white-space: pre-wrap;'>{System.Net.WebUtility.HtmlEncode(textBody)}</div>";
            }

            if (!string.IsNullOrEmpty(textBody))
            {
                textBody = textBanner + textBody;
            }
        }

        // Forward via BCC with Reply-To set to original sender.
        // Sender is added as To so they receive a copy in their own inbox.
        var success = await _emailService.SendBccEmailAsync(
            validRecipientEmails,
            email.Subject,
            htmlBody,
            textBody,
            email.FromAddress,
            email.FromName,
            cancellationToken,
            fromEmail: _forwardingFromAddress,
            fromName: "Vernetzt",
            attachments: email.Attachments,
            toEmail: token.SenderEmail,
            toName: email.FromName);

        if (success)
        {
            // Mark token as used
            token.IsUsed = true;
            token.UsedAt = DateTime.UtcNow;
            token.ForwardedToCount = validRecipientEmails.Count;
            token.UpdatedAt = DateTime.UtcNow;
            await _tokenRepository.UpdateAsync(token, cancellationToken);

            // Send summary reply to sender
            if (_sendSummaryReply)
            {
                await _emailService.SendForwardingSuccessSummaryAsync(
                    token.SenderEmail,
                    forwardedRecipientDisplayItems: recipientDeliveries
                        .Where(r => r.IsValidEmail)
                        .Select(r => r.DisplayName)
                        .ToList(),
                    droppedRecipientDisplayItems: recipientDeliveries
                        .Where(r => !r.IsValidEmail)
                        .Select(r => r.DisplayName)
                        .ToList(),
                    originalSubject: email.Subject,
                    originalFromAddress: email.FromAddress,
                    originalFromName: email.FromName,
                    originalHtmlBody: originalHtmlBody,
                    originalTextBody: originalTextBody,
                    cancellationToken: cancellationToken);
            }

            // Audit log — external sender address is masked (DSGVO/GDPR compliance)
            var recipientAudit = recipientDeliveries
                .Where(r => r.IsValidEmail)
                .Select(r => new
                {
                    Name = r.DisplayName,
                    EmailMasked = MaskAddress(r.Email)
                })
                .ToList();

            await _auditRepository.LogAsync(
                "EmailRelayToken",
                token.Id,
                AuditAction.EmailRelayForward,
                token.CreatedByUserId,
                null,
                JsonSerializer.Serialize(new
                {
                    Subject = email.Subject,
                    IncomingFrom = new
                    {
                        Name = email.FromName,
                        EmailMasked = MaskAddress(email.FromAddress)
                    },
                    RelayOwnerUserId = token.CreatedByUserId,
                    RelaySenderEmailMasked = MaskAddress(token.SenderEmail),
                    RecipientCount = validRecipientEmails.Count,
                    Recipients = recipientAudit
                }),
                cancellationToken);

            _logger.LogInformation(
                "Email forwarded via relay: TokenId={TokenId}, From={From}, Recipients={Count}, Subject={Subject}",
                token.Id, MaskAddress(email.FromAddress), validRecipientEmails.Count, LogSanitizer.Clean(email.Subject));
            _metrics?.RelayEmailProcessed(VernetztMetrics.RelayOutcomes.Forwarded);
        }
        else
        {
            _logger.LogError(
                "Failed to forward email via relay: TokenId={TokenId}, From={From}, Recipients={Count}, Subject={Subject}",
                token.Id, MaskAddress(email.FromAddress), validRecipientEmails.Count, LogSanitizer.Clean(email.Subject));
            _metrics?.RelayEmailProcessed(VernetztMetrics.RelayOutcomes.ForwardFailed);
        }
    }

    private async Task SendFailureNotificationAsync(
        string senderEmail,
        string reason,
        CancellationToken cancellationToken,
        string hint = "Bitte erstelle in der Vernetzt-App einen neuen Weiterleitungs-Token und versuche es erneut.",
        string? originalSubject = null)
    {
        var subject = "E-Mail-Weiterleitung fehlgeschlagen — Vernetzt";
        var subjectHtml = string.IsNullOrWhiteSpace(originalSubject)
            ? string.Empty
            : $"<p><strong>Betreff:</strong> {System.Net.WebUtility.HtmlEncode(originalSubject)}</p>";
        var subjectText = string.IsNullOrWhiteSpace(originalSubject) ? string.Empty : $"Betreff: {originalSubject}\n\n";

        var contentHtml = $@"
        <div class='alert alert-error' style='background-color: #f8d7da; padding: 15px; border-radius: 4px; border-left: 4px solid #dc3545; color: #721c24; margin-bottom: 20px;'>
            <strong>✗ Deine E-Mail konnte nicht weitergeleitet werden.</strong>
        </div>
        <p><strong>Grund:</strong> {System.Net.WebUtility.HtmlEncode(reason)}</p>
        {subjectHtml}
        <p>{System.Net.WebUtility.HtmlEncode(hint)}</p>
        <div class='footer'>
            <p>Dies ist eine automatisch generierte E-Mail. Bitte antworte nicht darauf.</p>
        </div>";

        var htmlBody = EmailTemplateHelper.WrapInTemplate(_baseUrl, contentHtml);

        var plainTextBody = $@"Vernetzt — E-Mail-Weiterleitung

✗ Deine E-Mail konnte nicht weitergeleitet werden.

Grund: {reason}

{subjectText}{hint}

Dies ist eine automatisch generierte E-Mail.";

        await _emailService.SendEmailAsync(senderEmail, subject, htmlBody, plainTextBody, cancellationToken);
    }

    private string GenerateInfoBanner(string fromAddress, string fromName, int recipientCount)
    {
        var senderDisplay = !string.IsNullOrEmpty(fromName)
            ? $"{System.Net.WebUtility.HtmlEncode(fromName)} ({System.Net.WebUtility.HtmlEncode(fromAddress)})"
            : System.Net.WebUtility.HtmlEncode(fromAddress);

        return $@"
<div style='background-color: #e3f2fd; padding: 12px; border-radius: 4px; border-left: 4px solid #0D4A70; margin-bottom: 20px; font-family: Arial, sans-serif; font-size: 13px; color: #333;'>
    <strong>Diese E-Mail wurde über Vernetzt an {recipientCount} Empfänger in BCC gesendet.</strong><br>
    Von: {senderDisplay}<br>
    <em style='font-size: 11px; color: #666;'>Antworten gehen direkt an den Absender (Reply-To).</em>
</div>
";
    }

    private string GeneratePlainTextInfoBanner(string fromAddress, string fromName, int recipientCount)
    {
        var senderDisplay = !string.IsNullOrEmpty(fromName)
            ? $"{fromName} ({fromAddress})"
            : fromAddress;

        return $"--- Vernetzt-Weiterleitung (an {recipientCount} Empfänger in BCC gesendet) ---\nVon: {senderDisplay}\nAntworten gehen direkt an den Absender.\n---\n\n";
    }

    private async Task<List<RelayRecipientDelivery>> ResolveRecipientDeliveriesAsync(
        IEnumerable<string> recipientEmails,
        CancellationToken cancellationToken)
    {
        var deliveries = new List<RelayRecipientDelivery>();
        var normalizedValidEmails = recipientEmails
            .Select(raw => raw?.Trim() ?? string.Empty)
            .Where(email => !string.IsNullOrWhiteSpace(email) && MailAddress.TryCreate(email, out _))
            .Select(EmailAddressHelper.Normalize)
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var resolvedDisplayItems = await _recipientResolutionService.ResolveDisplayItemsByEmailAsync(
            normalizedValidEmails,
            cancellationToken);

        var displayByEmail = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < normalizedValidEmails.Count; i++)
        {
            var email = normalizedValidEmails[i];
            var display = i < resolvedDisplayItems.Count ? resolvedDisplayItems[i] : "Unbekannter Kontakt";
            displayByEmail[email] = string.IsNullOrWhiteSpace(display) ? "Unbekannter Kontakt" : display;
        }

        foreach (var rawEmail in recipientEmails)
        {
            var email = rawEmail?.Trim() ?? string.Empty;
            var isValidEmail = !string.IsNullOrWhiteSpace(email) && MailAddress.TryCreate(email, out _);
            var normalized = EmailAddressHelper.Normalize(email) ?? string.Empty;
            var displayName = displayByEmail.TryGetValue(normalized, out var resolvedName)
                ? resolvedName
                : "Unbekannter Kontakt";

            deliveries.Add(new RelayRecipientDelivery(email, displayName, isValidEmail));
        }

        return deliveries;
    }

    private string GenerateTokenAddress(string tokenHex)
    {
        if (_addressingMode.Equals("CatchAll", StringComparison.OrdinalIgnoreCase))
        {
            // CatchAll: token@relay.example.com
            var domain = _baseAddress.Contains('@') ? _baseAddress.Split('@')[1] : _baseAddress;
            return $"{tokenHex}@{domain}";
        }
        else
        {
            // PlusAddressing: relay+token@example.com
            var atIndex = _baseAddress.IndexOf('@');
            if (atIndex < 0)
            {
                throw new InvalidOperationException("EmailRelay:BaseAddress must contain '@' for PlusAddressing mode");
            }
            var localPart = _baseAddress[..atIndex];
            var domain = _baseAddress[(atIndex + 1)..];
            return $"{localPart}+{tokenHex}@{domain}";
        }
    }

    private static string GenerateSecureToken()
    {
        var bytes = new byte[16]; // 16 bytes = 32 hex characters
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string? ResolveForwardingFromAddress(string baseAddress)
    {
        return MailAddress.TryCreate(baseAddress, out var validAddress)
            ? validAddress.Address
            : null;
    }

    private static string MaskAddress(string email) => EmailAddressHelper.Mask(email);

    private async Task TryLogAuditAsync(
        string entityType,
        Guid entityId,
        AuditAction action,
        Guid userId,
        string? newValues,
        string? notes,
        CancellationToken cancellationToken)
    {
        try
        {
            await _auditRepository.LogAsync(
                new AuditLog
                {
                    EntityType = entityType,
                    EntityId = entityId,
                    Action = action,
                    UserId = userId,
                    NewValues = newValues,
                    Notes = notes,
                    Timestamp = DateTime.UtcNow
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write relay audit log entry {Action}", action);
        }
    }
}
