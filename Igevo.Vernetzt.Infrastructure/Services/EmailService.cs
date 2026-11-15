using System.Net.Mail;
using System.Text.RegularExpressions;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Igevo.Vernetzt.Infrastructure.Services;

/// <summary>
/// Builds and dispatches email messages via <see cref="ISmtpSender"/>.
/// Responsible for template rendering, validation, and logging — not for SMTP mechanics.
/// </summary>
public class EmailService : IEmailService
{
    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private readonly ILogger<EmailService> _logger;
    private readonly IAppSettingsService _appSettingsService;
    private readonly ISmtpSender _smtpSender;
    private readonly string _fromEmail;
    private readonly string _fromName;
    private readonly string _baseUrl;

    /// <summary>
    /// Generates the email footer with contact person info (from DB settings) and legal text.
    /// </summary>
    private async Task<string> GetEmailFooterAsync(CancellationToken cancellationToken = default)
    {
        var contactName = await _appSettingsService.GetValueAsync("ContactName", cancellationToken);
        var contactEmail = await _appSettingsService.GetValueAsync("ContactEmail", cancellationToken);

        var contactHtml = "";
        if (!string.IsNullOrEmpty(contactName) && !string.IsNullOrEmpty(contactEmail))
        {
            contactHtml = $@"
            <p>Bei Fragen wende dich an <strong>{System.Net.WebUtility.HtmlEncode(contactName)}</strong>
               (<a href='mailto:{System.Net.WebUtility.HtmlEncode(contactEmail)}'>{System.Net.WebUtility.HtmlEncode(contactEmail)}</a>).</p>";
        }

        return $@"
        <div class='footer'>
            {contactHtml}
            <p>Dies ist eine automatisch generierte E-Mail. Bitte antworte nicht darauf.</p>
        </div>";
    }

    /// <summary>
    /// Plain text version of the footer with contact info.
    /// </summary>
    private async Task<string> GetPlainTextFooterAsync(CancellationToken cancellationToken = default)
    {
        var contactName = await _appSettingsService.GetValueAsync("ContactName", cancellationToken);
        var contactEmail = await _appSettingsService.GetValueAsync("ContactEmail", cancellationToken);

        var contactText = "";
        if (!string.IsNullOrEmpty(contactName) && !string.IsNullOrEmpty(contactEmail))
        {
            contactText = $"Bei Fragen wende dich an {contactName} ({contactEmail}).\n";
        }

        return $@"
{contactText}Dies ist eine automatisch generierte E-Mail. Bitte antworte nicht darauf.
";
    }

    internal EmailService(
        IConfiguration configuration,
        ILogger<EmailService> logger,
        IAppSettingsService appSettingsService,
        ISmtpSender smtpSender)
    {
        _logger = logger;
        _appSettingsService = appSettingsService;
        _smtpSender = smtpSender;

        _fromEmail = configuration["Email:From:Address"]
            ?? throw new InvalidOperationException("Email:From:Address is required in configuration");
        _fromName = configuration["Email:From:Name"]
            ?? throw new InvalidOperationException("Email:From:Name is required in configuration");

        _baseUrl = configuration["App:BaseUrl"]?.TrimEnd('/') ?? "";

        // LogDebug intentionally: EmailService is Scoped and re-instantiated on every polling cycle;
        // Info-level here would flood the log with an entry per mailbox poll.
        _logger.LogDebug(
            "Email service initialized: From={From}, BaseUrl={BaseUrl}",
            MaskAddress(_fromEmail),
            _baseUrl);
    }

    public async Task<bool> SendEmailAsync(
        string to,
        string subject,
        string htmlBody,
        string? plainTextBody = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(to))
        {
            _logger.LogWarning("Cannot send email: recipient address is empty");
            return false;
        }

        try
        {
            using var mailMessage = new MailMessage
            {
                From = new MailAddress(_fromEmail, _fromName),
                Subject = subject
            };

            mailMessage.To.Add(to);

            if (!string.IsNullOrWhiteSpace(plainTextBody))
            {
                var plainView = AlternateView.CreateAlternateViewFromString(plainTextBody, null, "text/plain");
                mailMessage.AlternateViews.Add(plainView);
            }

            if (!string.IsNullOrWhiteSpace(htmlBody))
            {
                var htmlView = AlternateView.CreateAlternateViewFromString(htmlBody, null, "text/html");
                mailMessage.AlternateViews.Add(htmlView);
            }

            await _smtpSender.SendAsync(mailMessage, cancellationToken);

            _logger.LogInformation(
                "Email sent: To={To}, Subject={Subject}",
                MaskAddress(to),
                LogSanitizer.Clean(subject));

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to send email: To={To}, Subject={Subject}, Error={ErrorMessage}",
                MaskAddress(to),
                LogSanitizer.Clean(subject),
                ex.Message);
            return false;
        }
    }

    public async Task<int> SendBulkEmailAsync(
        IEnumerable<string> recipients,
        string subject,
        string htmlBody,
        string? plainTextBody = null,
        CancellationToken cancellationToken = default)
    {
        var recipientList = recipients.ToList();
        if (recipientList.Count == 0)
        {
            _logger.LogWarning("Cannot send bulk email: no recipients provided");
            return 0;
        }

        _logger.LogInformation(
            "Starting bulk email send: Recipients={Count}, Subject={Subject}",
            recipientList.Count,
            LogSanitizer.Clean(subject));

        var successCount = 0;

        foreach (var recipient in recipientList)
        {
            var success = await SendEmailAsync(recipient, subject, htmlBody, plainTextBody, cancellationToken);
            if (success)
            {
                successCount++;
            }

            // Small delay to avoid overwhelming SMTP server
            await Task.Delay(100, cancellationToken);
        }

        _logger.LogInformation(
            "Bulk email completed: Success={Success}/{Total}, Subject={Subject}",
            successCount,
            recipientList.Count,
            LogSanitizer.Clean(subject));

        return successCount;
    }

    public async Task<bool> SendBccEmailAsync(
        IEnumerable<string> recipients,
        string subject,
        string htmlBody,
        string? plainTextBody = null,
        string? replyToEmail = null,
        string? replyToName = null,
        CancellationToken cancellationToken = default,
        string? fromEmail = null,
        string? fromName = null,
        IEnumerable<Core.DTOs.EmailRelay.EmailAttachment>? attachments = null,
        string? toEmail = null,
        string? toName = null)
    {
        var recipientList = recipients.ToList();
        if (recipientList.Count == 0)
        {
            _logger.LogWarning("Cannot send BCC email: no recipients provided");
            return false;
        }

        try
        {
            var senderEmail = fromEmail ?? _fromEmail;
            var senderName = fromName ?? _fromName;
            var senderDisplayName = SanitizeDisplayName(senderName);

            var senderAddress = TryCreateMailAddress(senderEmail, senderDisplayName);
            if (senderAddress == null)
            {
                _logger.LogWarning(
                    "Invalid sender address override detected, falling back to configured default: RequestedFrom={RequestedFrom}",
                    MaskAddress(senderEmail));

                senderDisplayName = SanitizeDisplayName(_fromName);
                senderAddress = TryCreateMailAddress(_fromEmail, senderDisplayName);
            }

            if (senderAddress == null)
            {
                _logger.LogError(
                    "Cannot send BCC email: configured default sender address is invalid: From={From}",
                    MaskAddress(_fromEmail));
                return false;
            }

            using var mailMessage = new MailMessage
            {
                From = senderAddress,
                Subject = subject
            };

            // Add explicit To recipient (e.g. the original sender in relay/bulk mails)
            if (!string.IsNullOrWhiteSpace(toEmail))
            {
                var toDisplayName = SanitizeDisplayName(toName);
                var toAddress = TryCreateMailAddress(toEmail, toDisplayName);
                if (toAddress != null)
                    mailMessage.To.Add(toAddress);
                else
                    _logger.LogWarning("Skipping invalid To address: {Email}", MaskAddress(toEmail));
            }

            // Add all recipients as BCC to hide them from each other
            foreach (var recipient in recipientList)
            {
                if (!string.IsNullOrWhiteSpace(recipient))
                {
                    var bccAddress = TryCreateMailAddress(recipient, null);
                    if (bccAddress != null)
                        mailMessage.Bcc.Add(bccAddress);
                    else
                        _logger.LogWarning("Skipping invalid BCC address: {Email}", MaskAddress(recipient));
                }
            }

            if (mailMessage.Bcc.Count == 0 && mailMessage.To.Count == 0)
            {
                _logger.LogWarning("Cannot send BCC email: no valid recipients after filtering");
                return false;
            }

            // Set Reply-To if provided
            if (!string.IsNullOrWhiteSpace(replyToEmail))
            {
                var replyToDisplayName = SanitizeDisplayName(replyToName);
                var replyTo = TryCreateMailAddress(replyToEmail, replyToDisplayName);
                if (replyTo != null)
                    mailMessage.ReplyToList.Add(replyTo);
                else
                    _logger.LogWarning("Skipping invalid Reply-To address: {Email}", MaskAddress(replyToEmail));
            }

            // Add attachments if provided
            if (attachments != null)
            {
                foreach (var attachment in attachments)
                {
                    var stream = new MemoryStream(attachment.Content);
                    var mailAttachment = new Attachment(stream, attachment.FileName, attachment.ContentType);
                    mailMessage.Attachments.Add(mailAttachment);
                }
            }

            if (!string.IsNullOrWhiteSpace(plainTextBody))
            {
                var plainView = AlternateView.CreateAlternateViewFromString(
                    plainTextBody,
                    null,
                    "text/plain");
                mailMessage.AlternateViews.Add(plainView);
            }
            if (!string.IsNullOrWhiteSpace(htmlBody))
            {
                var htmlView = AlternateView.CreateAlternateViewFromString(htmlBody, null, "text/html");
                mailMessage.AlternateViews.Add(htmlView);
            }

            await _smtpSender.SendAsync(mailMessage, cancellationToken);

            _logger.LogInformation(
                "BCC email sent successfully: Recipients={Count}, Subject={Subject}, ReplyTo={ReplyTo}, From={From}",
                mailMessage.Bcc.Count,
                LogSanitizer.Clean(subject),
                replyToEmail != null ? MaskAddress(replyToEmail) : "none",
                MaskAddress(mailMessage.From.Address));

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to send BCC email: Recipients={Count}, Subject={Subject}, Error={ErrorMessage}",
                recipientList.Count,
                LogSanitizer.Clean(subject),
                ex.Message);
            return false;
        }
    }

    public async Task<bool> SendForwardingSuccessSummaryAsync(
        string senderEmail,
        IReadOnlyCollection<string> forwardedRecipientDisplayItems,
        IReadOnlyCollection<string> droppedRecipientDisplayItems,
        string originalSubject,
        string originalFromAddress,
        string? originalFromName,
        string? originalHtmlBody,
        string? originalTextBody,
        CancellationToken cancellationToken = default)
    {
        var normalizedSubject = string.IsNullOrWhiteSpace(originalSubject)
            ? "(ohne Betreff)"
            : originalSubject.Trim();

        var originalSenderDisplay = !string.IsNullOrWhiteSpace(originalFromName)
            ? System.Net.WebUtility.HtmlEncode(originalFromName)
            : System.Net.WebUtility.HtmlEncode(originalFromAddress);

        var recipientCount = forwardedRecipientDisplayItems.Count;
        var forwardedNamesHtml = BuildNameListHtml(forwardedRecipientDisplayItems);
        var droppedNamesHtml = BuildNameListHtml(droppedRecipientDisplayItems);
        var originalMessageHtml = BuildOriginalMessageHtml(originalHtmlBody, originalTextBody);
        var timestamp = DateTime.UtcNow.ToString("dd.MM.yyyy HH:mm") + " UTC";
        var subject = $"Vernetzt: {normalizedSubject} — Weiterleitung erfolgreich";

        var contentHtml = $@"
        <div class='alert alert-success' style='background-color: #d4edda; padding: 15px; border-radius: 4px; border-left: 4px solid #28a745; color: #155724; margin-bottom: 20px;'>
            <strong>✓ Deine E-Mail wurde erfolgreich weitergeleitet.</strong>
        </div>
        <table role='presentation' style='width: 100%; border-collapse: collapse; margin-bottom: 20px;'>
            <tr><td class='muted' style='padding: 8px 12px 8px 0; vertical-align: top; white-space: nowrap;'>Empfänger:</td><td style='padding: 8px 0;'><strong>{recipientCount} Personen</strong></td></tr>
            <tr><td class='muted' style='padding: 8px 12px 8px 0; vertical-align: top; white-space: nowrap;'>Empfängerliste:</td><td style='padding: 8px 0;'>{forwardedNamesHtml}</td></tr>
            <tr><td class='muted' style='padding: 8px 12px 8px 0; vertical-align: top; white-space: nowrap;'>Zeitpunkt:</td><td style='padding: 8px 0;'>{timestamp}</td></tr>
            <tr><td class='muted' style='padding: 8px 12px 8px 0; vertical-align: top; white-space: nowrap;'>Betreff:</td><td style='padding: 8px 0;'>{System.Net.WebUtility.HtmlEncode(normalizedSubject)}</td></tr>
        </table>
        {(droppedRecipientDisplayItems.Count > 0
            ? $"<div class='alert alert-warning' style='background-color: #fff3cd; padding: 15px; border-radius: 4px; border-left: 4px solid #ff9800; color: #856404; margin-bottom: 20px;'><strong>Nicht weitergeleitete Empfänger (ungültige E-Mail-Adresse):</strong>{droppedNamesHtml}</div>"
            : string.Empty)}
        <div class='info-box'>
            <strong>Datenschutzhinweis:</strong> Die Empfänger haben deine E-Mail als BCC erhalten und können die anderen Empfänger nicht sehen.
        </div>
        <div class='divider'>
            <h3 style='margin: 0 0 12px 0; font-size: 16px;'>Ursprüngliche Nachricht</h3>
            <p class='muted' style='margin: 0 0 12px 0;'><strong>Von:</strong> {originalSenderDisplay}</p>
            {originalMessageHtml}
        </div>
        <div class='footer'>
            <p>Dies ist eine automatisch generierte Bestätigung. Bitte antworte nicht auf diese E-Mail.</p>
        </div>";

        var htmlBody = EmailTemplateHelper.WrapInTemplate(_baseUrl, contentHtml);

        var plainTextBody = $@"Vernetzt — E-Mail-Weiterleitung

✓ Deine E-Mail wurde erfolgreich weitergeleitet.

Empfänger: {recipientCount} Personen
Empfängerliste:
{BuildNameListPlainText(forwardedRecipientDisplayItems)}
Zeitpunkt: {timestamp}
Betreff: {normalizedSubject}

{(droppedRecipientDisplayItems.Count > 0
    ? $"Nicht weitergeleitete Empfänger (ungültige E-Mail-Adresse):\n{BuildNameListPlainText(droppedRecipientDisplayItems)}\n"
    : string.Empty)}

Datenschutzhinweis: Die Empfänger haben deine E-Mail als BCC erhalten und können die anderen Empfänger nicht sehen.

--- Ursprüngliche Nachricht ---
Von: {(!string.IsNullOrWhiteSpace(originalFromName) ? originalFromName : originalFromAddress)}
{BuildOriginalMessagePlainText(originalTextBody, originalHtmlBody)}

Dies ist eine automatisch generierte Bestätigung.";

        return await SendEmailAsync(senderEmail, subject, htmlBody, plainTextBody, cancellationToken);
    }

    public async Task<bool> SendMagicLinkEmailAsync(
        string to,
        string magicLink,
        int expirationMinutes,
        CancellationToken cancellationToken = default)
    {
        var subject = "Dein Anmeldelink für Vernetzt";
        var footer = await GetEmailFooterAsync(cancellationToken);
        var plainFooter = await GetPlainTextFooterAsync(cancellationToken);

        var contentHtml = $@"
        <h2>Anmeldung bei Vernetzt</h2>
        <p>Du hast eine Anmeldung bei Vernetzt angefordert.</p>
        <p>Klicke auf den folgenden Button, um dich anzumelden:</p>
        {EmailTemplateHelper.Button(magicLink, "Jetzt anmelden")}
        <p>Oder kopiere diesen Link in deinen Browser:</p>
        <p class='link-box'><a href='{magicLink}'>{magicLink}</a></p>
        <p><strong>Wichtig:</strong> Dieser Link ist nur {expirationMinutes} Minuten gültig und kann nur einmal verwendet werden.</p>
        <p>Wenn du diese Anmeldung nicht angefordert hast, ignoriere diese E-Mail bitte.</p>
        {footer}";

        var htmlBody = EmailTemplateHelper.WrapInTemplate(_baseUrl, contentHtml);

        var plainTextBody = $@"Anmeldung bei Vernetzt

Du hast eine Anmeldung bei Vernetzt angefordert.

Verwende den folgenden Link, um dich anzumelden:
{magicLink}

WICHTIG: Dieser Link ist nur {expirationMinutes} Minuten gültig und kann nur einmal verwendet werden.

Wenn du diese Anmeldung nicht angefordert hast, ignoriere diese E-Mail bitte.
{plainFooter}";

        _logger.LogInformation(
            "Sending magic link email: To={To}, ExpiresIn={Minutes}m",
            MaskAddress(to),
            expirationMinutes);

        return await SendEmailAsync(to, subject, htmlBody, plainTextBody, cancellationToken);
    }

    public async Task<bool> SendUserCreatedNotificationAsync(
        string to,
        string userName,
        string? loginUrl = null,
        CancellationToken cancellationToken = default)
    {
        var subject = "Willkommen bei Vernetzt";
        var footer = await GetEmailFooterAsync(cancellationToken);
        var plainFooter = await GetPlainTextFooterAsync(cancellationToken);

        var effectiveLoginUrl = loginUrl ?? $"{_baseUrl}/login";
        var loginButton = !string.IsNullOrEmpty(_baseUrl) || !string.IsNullOrEmpty(loginUrl)
            ? EmailTemplateHelper.Button(effectiveLoginUrl, "Zur Anmeldung")
            : "";

        var contentHtml = $@"
        <h2>Willkommen bei Vernetzt!</h2>
        <p>Hallo {System.Net.WebUtility.HtmlEncode(userName)},</p>
        <p>dein Benutzerkonto wurde erfolgreich erstellt.</p>
        <p>Du kannst dich jetzt mit deiner E-Mail-Adresse anmelden. Du erhältst bei jeder Anmeldung einen einmaligen Anmeldelink per E-Mail.</p>
        {loginButton}
        {footer}";

        var htmlBody = EmailTemplateHelper.WrapInTemplate(_baseUrl, contentHtml);

        var plainTextBody = $@"Willkommen bei Vernetzt!

Hallo {userName},

dein Benutzerkonto wurde erfolgreich erstellt.

Du kannst dich jetzt mit deiner E-Mail-Adresse anmelden. Du erhältst bei jeder Anmeldung einen einmaligen Anmeldelink per E-Mail.

Zur Anmeldung: {effectiveLoginUrl}
{plainFooter}";

        _logger.LogInformation(
            "Sending user created notification: To={To}, UserName={Name}",
            MaskAddress(to),
            userName);

        return await SendEmailAsync(to, subject, htmlBody, plainTextBody, cancellationToken);
    }

    public async Task<bool> SendClubAssignmentConsentRequestAsync(
        string to,
        string userName,
        string clubName,
        Guid assignmentId,
        bool canEdit = false,
        CancellationToken cancellationToken = default)
    {
        string Html(string value) => System.Net.WebUtility.HtmlEncode(value);
        return await SendAssignmentConsentRequestAsync(
            to,
            userName,
            subject: $"Zustimmen oder ablehnen: Zuordnung zum Verein {clubName}",
            locationHtml: $"dem Verein <strong>{Html(clubName)}</strong>",
            locationPlain: $"dem Verein \"{clubName}\"",
            kind: "Verein",
            assignmentId,
            canEdit,
            cancellationToken);
    }

    public async Task<bool> SendDepartmentAssignmentConsentRequestAsync(
        string to,
        string userName,
        string departmentName,
        string clubName,
        Guid assignmentId,
        bool canEdit = false,
        string? parentDepartmentName = null,
        CancellationToken cancellationToken = default)
    {
        var isGroup = !string.IsNullOrWhiteSpace(parentDepartmentName);
        var kind = isGroup ? "Gruppe" : "Abteilung";

        // Always name the full hierarchy: "der Gruppe X (Abteilung Y) im Verein Z"
        string Html(string value) => System.Net.WebUtility.HtmlEncode(value);
        var locationHtml = isGroup
            ? $"der Gruppe <strong>{Html(departmentName)}</strong> (Abteilung <strong>{Html(parentDepartmentName!)}</strong>) im Verein <strong>{Html(clubName)}</strong>"
            : $"der Abteilung <strong>{Html(departmentName)}</strong> im Verein <strong>{Html(clubName)}</strong>";
        var locationPlain = isGroup
            ? $"der Gruppe \"{departmentName}\" (Abteilung \"{parentDepartmentName}\") im Verein \"{clubName}\""
            : $"der Abteilung \"{departmentName}\" im Verein \"{clubName}\"";

        return await SendAssignmentConsentRequestAsync(
            to,
            userName,
            subject: $"Zustimmen oder ablehnen: Zuordnung zur {kind} {departmentName}",
            locationHtml,
            locationPlain,
            kind,
            assignmentId,
            canEdit,
            cancellationToken);
    }

    /// <summary>
    /// Asks the assigned user to consent to a new assignment and to choose who may see it.
    /// Until then the assignment is only visible internally and grants no rights.
    /// </summary>
    private async Task<bool> SendAssignmentConsentRequestAsync(
        string to,
        string userName,
        string subject,
        string locationHtml,
        string locationPlain,
        string kind,
        Guid assignmentId,
        bool canEdit,
        CancellationToken cancellationToken)
    {
        var footer = await GetEmailFooterAsync(cancellationToken);
        var plainFooter = await GetPlainTextFooterAsync(cancellationToken);

        var consentUrl = $"{_baseUrl}/konto?tab=zuordnungen&zuordnung={assignmentId}";
        var consentButton = !string.IsNullOrEmpty(_baseUrl)
            ? EmailTemplateHelper.Button(consentUrl, "Zustimmen oder ablehnen")
            : "";
        var rights = canEdit
            ? $"Nach deiner Zustimmung kannst du die Daten dieser Zuordnung ({kind}) bearbeiten."
            : "Nach deiner Zustimmung bist du als Ansprechperson eingetragen.";
        const string pendingHint = "Bis zu deiner Zustimmung sehen dich dort nur die Verwaltung und die Personen, die diese Organisation bearbeiten dürfen. Mails an die Zuordnung und Bearbeitungsrechte sind so lange noch nicht aktiv.";
        const string decisionHint = "Bitte entscheide, ob du der Zuordnung zustimmst oder sie ablehnst. Passt sie nicht, lehne sie ab: Dann wird sie entfernt und die Person, die dich eingetragen hat, informiert.";
        const string visibilityHint = "Stimmst du zu, legst du fest, wer die Zuordnung und eine Amtsadresse sehen darf: nur die Verwaltung, alle registrierten Benutzer oder öffentlich. Das kannst du später jederzeit unter „Mein Konto → Zuordnungen“ ändern.";

        var contentHtml = $@"
        <h2>Neue Zuordnung: zustimmen oder ablehnen</h2>
        <p>Hallo {System.Net.WebUtility.HtmlEncode(userName)},</p>
        <p>du wurdest {locationHtml} als Kontaktperson zugeordnet.</p>
        <p>{rights}</p>
        <div class='info-box'>
            <strong>Deine Zustimmung fehlt noch.</strong> {pendingHint}
        </div>
        <p><strong>{decisionHint}</strong></p>
        <p>{visibilityHint}</p>
        {consentButton}
        {footer}";

        var htmlBody = EmailTemplateHelper.WrapInTemplate(_baseUrl, contentHtml);

        var plainTextBody = $@"Neue Zuordnung: zustimmen oder ablehnen

Hallo {userName},

du wurdest {locationPlain} als Kontaktperson zugeordnet.

{rights}

Deine Zustimmung fehlt noch. {pendingHint}

{decisionHint}

{visibilityHint}

Zustimmen oder ablehnen: {consentUrl}
{plainFooter}";

        _logger.LogInformation(
            "Sending assignment consent request: To={To}, Assignment={AssignmentId}",
            MaskAddress(to),
            assignmentId);

        return await SendEmailAsync(to, subject, htmlBody, plainTextBody, cancellationToken);
    }

    public async Task<bool> SendAssignmentDeclinedNotificationAsync(
        string to,
        string recipientName,
        string declinedByName,
        string organizationName,
        CancellationToken cancellationToken = default)
    {
        var subject = $"Zuordnung abgelehnt: {organizationName}";
        var footer = await GetEmailFooterAsync(cancellationToken);
        var plainFooter = await GetPlainTextFooterAsync(cancellationToken);
        string Html(string value) => System.Net.WebUtility.HtmlEncode(value);

        var contentHtml = $@"
        <h2>Zuordnung abgelehnt</h2>
        <p>Hallo {Html(recipientName)},</p>
        <p><strong>{Html(declinedByName)}</strong> hat die Zuordnung zu <strong>{Html(organizationName)}</strong> abgelehnt. Die Zuordnung wurde entfernt.</p>
        <p>Falls das ein Versehen war, sprecht euch bitte ab und ordnet die Person danach erneut zu.</p>
        {footer}";

        var htmlBody = EmailTemplateHelper.WrapInTemplate(_baseUrl, contentHtml);
        var plainTextBody = $@"Zuordnung abgelehnt

Hallo {recipientName},

{declinedByName} hat die Zuordnung zu ""{organizationName}"" abgelehnt. Die Zuordnung wurde entfernt.

Falls das ein Versehen war, sprecht euch bitte ab und ordnet die Person danach erneut zu.
{plainFooter}";

        _logger.LogInformation("Sending assignment declined notification: To={To}", MaskAddress(to));
        return await SendEmailAsync(to, subject, htmlBody, plainTextBody, cancellationToken);
    }

    public async Task<bool> SendServerStartupNotificationAsync(
        string to,
        string serverInfo,
        CancellationToken cancellationToken = default)
    {
        var subject = "Vernetzt Server gestartet";
        var footer = await GetEmailFooterAsync(cancellationToken);
        var plainFooter = await GetPlainTextFooterAsync(cancellationToken);

        var contentHtml = $@"
        <h2>Server erfolgreich gestartet</h2>
        <p>Der Vernetzt-Server wurde erfolgreich gestartet.</p>
        <div class='info-box'>
            <strong>Server-Informationen:</strong><br>
            {serverInfo.Replace("\n", "<br>")}
        </div>
        <p>Diese E-Mail dient als Test der E-Mail-Funktionalität und zur Bestätigung, dass der Server betriebsbereit ist.</p>
        {footer}";

        var htmlBody = EmailTemplateHelper.WrapInTemplate(_baseUrl, contentHtml);

        var plainTextBody = $@"Server erfolgreich gestartet

Der Vernetzt-Server wurde erfolgreich gestartet.

Server-Informationen:
{serverInfo}

Diese E-Mail dient als Test der E-Mail-Funktionalität und zur Bestätigung, dass der Server betriebsbereit ist.
{plainFooter}";

        _logger.LogInformation(
            "Sending server startup notification: To={To}",
            MaskAddress(to));

        return await SendEmailAsync(to, subject, htmlBody, plainTextBody, cancellationToken);
    }

    public async Task<bool> SendInvitationEmailAsync(
        string to,
        string invitedByName,
        string onboardingUrl,
        string revokeUrl,
        int expirationDays,
        CancellationToken cancellationToken = default)
    {
        // Ensure URLs are absolute for email clients
        if (onboardingUrl.StartsWith('/'))
            onboardingUrl = $"{_baseUrl}{onboardingUrl}";
        if (revokeUrl.StartsWith('/'))
            revokeUrl = $"{_baseUrl}{revokeUrl}";

        var subject = "Einladung zu Vernetzt";
        var footer = await GetEmailFooterAsync(cancellationToken);
        var plainFooter = await GetPlainTextFooterAsync(cancellationToken);

        var contentHtml = $@"
        <h2>Du wurdest zu Vernetzt eingeladen</h2>
        <p><strong>{System.Net.WebUtility.HtmlEncode(invitedByName)}</strong> hat dich eingeladen, der Vereinsplattform <strong>Vernetzt</strong> beizutreten.</p>
        <p>Klicke auf den folgenden Button, um dein Profil zu erstellen und die Plattform zu nutzen:</p>
        {EmailTemplateHelper.Button(onboardingUrl, "Einladung annehmen")}
        <div class='info-box'>
            <strong>Hinweis:</strong> Diese Einladung ist <strong>{expirationDays} Tage</strong> gültig.
        </div>
        <p>Falls du diese Einladung nicht erwartet hast oder deine Daten löschen möchtest, kannst du die Einladung hier widerrufen:</p>
        <p><a href='{revokeUrl}'>Einladung widerrufen und Daten löschen</a></p>
        {footer}";

        var htmlBody = EmailTemplateHelper.WrapInTemplate(_baseUrl, contentHtml);

        var plainTextBody = $@"Du wurdest zu Vernetzt eingeladen

{invitedByName} hat dich eingeladen, der Vereinsplattform Vernetzt beizutreten.

Klicke auf den folgenden Link, um dein Profil zu erstellen:
{onboardingUrl}

Hinweis: Diese Einladung ist {expirationDays} Tage gültig.

Falls du diese Einladung nicht erwartet hast, kannst du sie hier widerrufen:
{revokeUrl}
{plainFooter}";

        _logger.LogInformation(
            "Sending invitation email: To={To}, InvitedBy={InvitedBy}",
            MaskAddress(to),
            invitedByName);

        return await SendEmailAsync(to, subject, htmlBody, plainTextBody, cancellationToken);
    }

    public async Task<bool> SendContactAssignmentEmailVerificationAsync(
        string to,
        string verifyUrl,
        CancellationToken cancellationToken = default)
    {
        var subject = "Amtsadresse bestätigen - Vernetzt";
        var footer = await GetEmailFooterAsync(cancellationToken);
        var plainFooter = await GetPlainTextFooterAsync(cancellationToken);

        var contentHtml = $@"
        <h2>Bitte bestätige diese Amtsadresse</h2>
        <p>Diese Adresse wurde in Vernetzt als <strong>Amtsadresse</strong> für eine Kontaktzuordnung hinterlegt. Nach der Bestätigung gehen Mails zu dieser Zuordnung hierhin.</p>
        <p>Klicke auf den folgenden Button, um die Adresse zu bestätigen:</p>
        {EmailTemplateHelper.Button(verifyUrl, "Amtsadresse bestätigen")}
        <p>Oder kopiere diesen Link in deinen Browser:</p>
        <p class='link-box'><a href='{verifyUrl}'>{verifyUrl}</a></p>
        <div class='info-box'>
            <strong>Hinweis:</strong> Der Link ist 72 Stunden gültig. Anmelden kann man sich mit einer Amtsadresse nicht – dafür gilt immer die persönliche Adresse.
            Wenn du diese Adresse nicht hinterlegen wolltest, kannst du diese E-Mail ignorieren.
        </div>
        {footer}";

        var htmlBody = EmailTemplateHelper.WrapInTemplate(_baseUrl, contentHtml);

        var plainTextBody = $@"Bitte bestätige diese Amtsadresse

Diese Adresse wurde in Vernetzt als Amtsadresse für eine Kontaktzuordnung hinterlegt. Nach der Bestätigung gehen Mails zu dieser Zuordnung hierhin.

Amtsadresse bestätigen:
{verifyUrl}

Hinweis: Der Link ist 72 Stunden gültig. Anmelden kann man sich mit einer Amtsadresse nicht – dafür gilt immer die persönliche Adresse.
Wenn du diese Adresse nicht hinterlegen wolltest, kannst du diese E-Mail ignorieren.
{plainFooter}";

        _logger.LogInformation("Sending contact-assignment email verification: To={To}", MaskAddress(to));
        return await SendEmailAsync(to, subject, htmlBody, plainTextBody, cancellationToken);
    }

    public async Task<bool> SendContextEmailLoginHintAsync(
        string to,
        CancellationToken cancellationToken = default)
    {
        var subject = "Hinweis zur Anmeldung bei Vernetzt";
        var footer = await GetEmailFooterAsync(cancellationToken);
        var plainFooter = await GetPlainTextFooterAsync(cancellationToken);
        var loginUrl = $"{_baseUrl}/login";

        var contentHtml = $@"
        <h2>Anmeldung mit dieser Adresse nicht möglich</h2>
        <p>Für diese E-Mail-Adresse wurde ein Anmeldelink bei Vernetzt angefordert.</p>
        <p>Die Adresse ist in Vernetzt als <strong>Amtsadresse</strong> einer Kontaktzuordnung hinterlegt, z. B. für den Vorstand. Über eine Amtsadresse kann man sich nicht anmelden.</p>
        <p>Bitte melde dich mit deiner <strong>persönlichen</strong> E-Mail-Adresse an:</p>
        {EmailTemplateHelper.Button(loginUrl, "Zur Anmeldung")}
        <div class='info-box'>
            <strong>Warum?</strong> Amtsadressen werden oft an die Nachfolge im Amt weitergegeben. Damit dadurch niemand Zugriff auf ein persönliches Konto bekommt, ist die Anmeldung nur mit der persönlichen Adresse möglich.
            Wenn du nicht weißt, welche Adresse hinterlegt ist, wende dich an die Verwaltung deines Vereins.
        </div>
        <p>Wenn du keine Anmeldung angefordert hast, kannst du diese E-Mail ignorieren.</p>
        {footer}";

        var htmlBody = EmailTemplateHelper.WrapInTemplate(_baseUrl, contentHtml);

        var plainTextBody = $@"Anmeldung mit dieser Adresse nicht möglich

Für diese E-Mail-Adresse wurde ein Anmeldelink bei Vernetzt angefordert.

Die Adresse ist in Vernetzt als Amtsadresse einer Kontaktzuordnung hinterlegt, z. B. für den Vorstand. Über eine Amtsadresse kann man sich nicht anmelden.

Bitte melde dich mit deiner persönlichen E-Mail-Adresse an:
{loginUrl}

Warum? Amtsadressen werden oft an die Nachfolge im Amt weitergegeben. Damit dadurch niemand Zugriff auf ein persönliches Konto bekommt, ist die Anmeldung nur mit der persönlichen Adresse möglich. Wenn du nicht weißt, welche Adresse hinterlegt ist, wende dich an die Verwaltung deines Vereins.

Wenn du keine Anmeldung angefordert hast, kannst du diese E-Mail ignorieren.
{plainFooter}";

        _logger.LogInformation("Sending context-email login hint: To={To}", MaskAddress(to));
        return await SendEmailAsync(to, subject, htmlBody, plainTextBody, cancellationToken);
    }

    public async Task<bool> SendAccountEmailChangeConfirmationAsync(
        string to,
        string accountName,
        string confirmUrl,
        CancellationToken cancellationToken = default)
    {
        var subject = "Neue Anmeldeadresse bestätigen - Vernetzt";
        var footer = await GetEmailFooterAsync(cancellationToken);
        var plainFooter = await GetPlainTextFooterAsync(cancellationToken);
        var nameHtml = System.Net.WebUtility.HtmlEncode(accountName);

        var contentHtml = $@"
        <h2>Bitte bestätige deine neue Anmeldeadresse</h2>
        <p>Für das Vernetzt-Konto von <strong>{nameHtml}</strong> soll diese Adresse künftig als persönliche Adresse zur Anmeldung dienen.</p>
        <p>Die Änderung wird erst wirksam, wenn du sie bestätigst:</p>
        {EmailTemplateHelper.Button(confirmUrl, "Neue Adresse bestätigen")}
        <p>Oder kopiere diesen Link in deinen Browser:</p>
        <p class='link-box'><a href='{confirmUrl}'>{confirmUrl}</a></p>
        <div class='info-box'>
            <strong>Hinweis:</strong> Der Link ist 72 Stunden gültig. Passkeys und bestehende Anmeldungen bleiben nach der Änderung gültig.
            Wenn du diese Änderung nicht erwartet hast, ignoriere diese E-Mail – dann bleibt alles, wie es ist.
        </div>
        {footer}";

        var htmlBody = EmailTemplateHelper.WrapInTemplate(_baseUrl, contentHtml);

        var plainTextBody = $@"Bitte bestätige deine neue Anmeldeadresse

Für das Vernetzt-Konto von {accountName} soll diese Adresse künftig als persönliche Adresse zur Anmeldung dienen.

Die Änderung wird erst wirksam, wenn du sie bestätigst:
{confirmUrl}

Hinweis: Der Link ist 72 Stunden gültig. Passkeys und bestehende Anmeldungen bleiben nach der Änderung gültig.
Wenn du diese Änderung nicht erwartet hast, ignoriere diese E-Mail – dann bleibt alles, wie es ist.
{plainFooter}";

        _logger.LogInformation("Sending account email change confirmation: To={To}", MaskAddress(to));
        return await SendEmailAsync(to, subject, htmlBody, plainTextBody, cancellationToken);
    }

    public async Task<bool> SendAccountEmailChangedNoticeAsync(
        string to,
        string accountName,
        string newEmailMasked,
        CancellationToken cancellationToken = default)
    {
        var subject = "Deine Anmeldeadresse wurde geändert - Vernetzt";
        var footer = await GetEmailFooterAsync(cancellationToken);
        var plainFooter = await GetPlainTextFooterAsync(cancellationToken);
        var nameHtml = System.Net.WebUtility.HtmlEncode(accountName);
        var newHtml = System.Net.WebUtility.HtmlEncode(newEmailMasked);

        var contentHtml = $@"
        <h2>Anmeldeadresse geändert</h2>
        <p>Die persönliche Adresse des Vernetzt-Kontos von <strong>{nameHtml}</strong> wurde geändert. Angemeldet wird ab jetzt mit <strong>{newHtml}</strong>; an diese Adresse gehen keine Anmeldelinks mehr.</p>
        <div class='info-box'>
            <strong>Das warst du nicht?</strong> Dann wende dich bitte umgehend an die Verwaltung deines Vereins oder die Administration von Vernetzt.
        </div>
        {footer}";

        var htmlBody = EmailTemplateHelper.WrapInTemplate(_baseUrl, contentHtml);

        var plainTextBody = $@"Anmeldeadresse geändert

Die persönliche Adresse des Vernetzt-Kontos von {accountName} wurde geändert. Angemeldet wird ab jetzt mit {newEmailMasked}; an diese Adresse gehen keine Anmeldelinks mehr.

Das warst du nicht? Dann wende dich bitte umgehend an die Verwaltung deines Vereins oder die Administration von Vernetzt.
{plainFooter}";

        _logger.LogInformation("Sending account email changed notice: To={To}", MaskAddress(to));
        return await SendEmailAsync(to, subject, htmlBody, plainTextBody, cancellationToken);
    }

    private static string BuildNameListHtml(IReadOnlyCollection<string> names)
    {
        if (names.Count == 0)
        {
            return "<span>Keine</span>";
        }

        var lines = names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => $"<div style='margin: 0 0 2px 0;'>{System.Net.WebUtility.HtmlEncode(name)}</div>")
            .ToList();

        return lines.Count == 0 ? "<span>Keine</span>" : string.Join(string.Empty, lines);
    }

    private static string BuildNameListPlainText(IReadOnlyCollection<string> names)
    {
        var lines = names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
        return lines.Count == 0 ? "Keine" : string.Join("\n", lines);
    }

    private static string BuildOriginalMessageHtml(string? originalHtmlBody, string? originalTextBody)
    {
        var readableText = BuildOriginalMessagePlainText(originalTextBody, originalHtmlBody);
        return $@"<pre class='original-message' style='margin: 0; padding: 12px; border: 1px solid #d1d5db; border-radius: 4px; background-color: #f8fafc; color: #111827; font-family: Arial, sans-serif; white-space: pre-wrap;'>{System.Net.WebUtility.HtmlEncode(readableText)}</pre>";
    }

    private static string BuildOriginalMessagePlainText(string? originalTextBody, string? originalHtmlBody)
    {
        if (!string.IsNullOrWhiteSpace(originalTextBody))
        {
            return originalTextBody.Trim();
        }

        if (!string.IsNullOrWhiteSpace(originalHtmlBody))
        {
            return HtmlToPlainText(originalHtmlBody);
        }

        return "[Kein Nachrichteninhalt verfügbar]";
    }

    private static string HtmlToPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return "[Kein Nachrichteninhalt verfügbar]";
        }

        var normalized = html
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</p>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</div>", "\n", StringComparison.OrdinalIgnoreCase);

        var stripped = HtmlTagRegex.Replace(normalized, string.Empty);
        var decoded = System.Net.WebUtility.HtmlDecode(stripped) ?? string.Empty;
        var cleaned = decoded.Replace("\r\n", "\n").Trim();

        return string.IsNullOrWhiteSpace(cleaned)
            ? "[Kein Nachrichteninhalt verfügbar]"
            : cleaned;
    }

    /// <summary>
    /// Tries to create a <see cref="MailAddress"/> from an address and optional display name.
    /// Returns <c>null</c> if the address is invalid instead of throwing.
    /// If the sanitized display name still causes a parse failure, retries with address-only.
    /// </summary>
    private static MailAddress? TryCreateMailAddress(string address, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(address))
            return null;

        // Try with display name first
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            if (MailAddress.TryCreate(address, displayName, out var result))
                return result;
        }

        // Fall back to address-only
        return MailAddress.TryCreate(address, out var addressOnly) ? addressOnly : null;
    }

    /// <summary>
    /// Strips characters from a display name that are RFC 2822 specials and cause
    /// <see cref="System.Net.Mail.MailAddress"/> to throw a <see cref="FormatException"/>.
    /// Affects externally-sourced names (e.g. the From-Name of a forwarded email).
    /// </summary>
    private static string? SanitizeDisplayName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        // RFC 2822 specials that confuse System.Net.Mail's address parser when
        // they appear unescaped in a display name: ( ) < > [ ] : ; @ \ "
        // Control characters (newlines etc.) are also stripped.
        var sanitized = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (c < 0x20 || c == 0x7F)  // control characters
                continue;
            if (c is '(' or ')' or '<' or '>' or '[' or ']' or ':' or ';' or '@' or '\\' or '"')
                continue;
            sanitized.Append(c);
        }

        var result = sanitized.ToString().Trim();
        return result.Length > 0 ? result : null;
    }

    private static string MaskAddress(string email) => EmailAddressHelper.Mask(email);
}
