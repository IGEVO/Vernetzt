using System.Security.Claims;
using System.Text.Json;
using Igevo.Vernetzt.Core.DTOs;
using Igevo.Vernetzt.Core.DTOs.Email;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ganss.Xss;

namespace Igevo.Vernetzt.Server.Controllers;

/// <summary>
/// Controller for email operations including bulk email sending.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class EmailController : ControllerBase
{
    private readonly IEmailService _emailService;
    private readonly IUserService _userService;
    private readonly IRecipientResolutionService _recipientResolutionService;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IHtmlSanitizer _htmlSanitizer;
    private readonly ILogger<EmailController> _logger;

    public EmailController(
        IEmailService emailService,
        IUserService userService,
        IRecipientResolutionService recipientResolutionService,
        IAuditLogRepository auditLogRepository,
        IHtmlSanitizer htmlSanitizer,
        ILogger<EmailController> logger)
    {
        _emailService = emailService;
        _userService = userService;
        _recipientResolutionService = recipientResolutionService;
        _auditLogRepository = auditLogRepository;
        _htmlSanitizer = htmlSanitizer;
        _logger = logger;
    }

    /// <summary>
    /// Sends a bulk email to multiple recipients via BCC.
    /// All recipients will receive the email in BCC to protect their privacy.
    /// </summary>
    /// <param name="request">Bulk email request with subject, body, and recipients</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>SendBulkEmailResponse with success status and recipient count</returns>
    [HttpPost("bulk")]
    [ProducesResponseType(typeof(ApiResponse<SendBulkEmailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<SendBulkEmailResponse>>> SendBulkEmail(
        [FromBody] SendBulkEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var resolution = await _recipientResolutionService.ResolveForSendAsync(
                GetUserId(),
                request.RecipientAssignmentIds,
                request.RecipientUserIds,
                cancellationToken);

            if (resolution.Forbidden)
            {
                return StatusCode(403, ApiResponse<SendBulkEmailResponse>.CreateError(
                    "Mindestens ein Empfänger ist nicht zugänglich.",
                    "FORBIDDEN_RECIPIENT_ASSIGNMENT"));
            }

            // Only platform contacts the sender may access; free-text addresses are not accepted
            var recipientEmails = resolution.RecipientEmails;

            _logger.LogInformation("Bulk email request received from user {UserId} for {RecipientCount} recipients",
                GetUserId(), recipientEmails.Count);

            // Validate request
            if (recipientEmails.Count == 0)
            {
                return BadRequest(ApiResponse<SendBulkEmailResponse>.CreateError(
                    "Mindestens ein Empfänger ist erforderlich.",
                    "NO_RECIPIENTS"));
            }

            // Get current user for reply-to (if not provided)
            var userId = GetUserId();
            var currentUser = await _userService.GetByIdAsync(userId, userId, cancellationToken);

            if (currentUser == null)
            {
                return Unauthorized(ApiResponse<SendBulkEmailResponse>.CreateError(
                    "Benutzer nicht gefunden.",
                    "USER_NOT_FOUND"));
            }

            // Reply-To and banner sender are always the authenticated user
            var replyToEmail = currentUser.Email;
            var replyToName = $"{currentUser.FirstName} {currentUser.LastName}";

            // Prepend a "sent via Vernetzt" info banner to the HTML body
            var senderDisplay = System.Net.WebUtility.HtmlEncode(replyToName);
            var infoBannerHtml = $@"<div style='background-color: #e3f2fd; padding: 12px; border-radius: 4px; border-left: 4px solid #0D4A70; margin-bottom: 20px; font-family: Arial, sans-serif; font-size: 13px; color: #333;'>
    <strong>Diese E-Mail wurde über Vernetzt an {recipientEmails.Count} Empfänger in BCC gesendet.</strong><br>
    Von: {senderDisplay}<br>
    <em style='font-size: 11px; color: #666;'>Antworten gehen direkt an den Absender (Reply-To).</em>
</div>
";
            var infoBannerText = $"--- Vernetzt-Weiterleitung (an {recipientEmails.Count} Empfänger in BCC gesendet) ---\nVon: {replyToName}\nAntworten gehen direkt an den Absender.\n---\n\n";

            var sanitizedHtmlBody = _htmlSanitizer.Sanitize(request.HtmlBody ?? string.Empty);
            var sanitizedPlainTextBody = string.IsNullOrWhiteSpace(request.PlainTextBody)
                ? HtmlToPlainText(sanitizedHtmlBody)
                : request.PlainTextBody;

            var htmlBodyWithBanner = string.IsNullOrWhiteSpace(sanitizedHtmlBody)
                ? infoBannerHtml
                : infoBannerHtml + sanitizedHtmlBody;
            var plainTextBodyWithBanner = string.IsNullOrWhiteSpace(sanitizedPlainTextBody)
                ? null
                : infoBannerText + sanitizedPlainTextBody;

            // Send email via BCC; sender (current user) is added to To so they receive a copy
            var success = await _emailService.SendBccEmailAsync(
                recipientEmails,
                request.Subject,
                htmlBodyWithBanner,
                plainTextBodyWithBanner,
                replyToEmail,
                replyToName,
                cancellationToken,
                toEmail: currentUser.Email,
                toName: $"{currentUser.FirstName} {currentUser.LastName}");

            var response = new SendBulkEmailResponse
            {
                Success = success,
                RecipientCount = success ? recipientEmails.Count : 0,
                ErrorMessage = success ? null : "E-Mail konnte nicht gesendet werden."
            };

            if (success)
            {
                var recipientAudit = recipientEmails
                    .Select(email => new
                    {
                        EmailMasked = EmailAddressHelper.Mask(email)
                    })
                    .ToList();

                await _auditLogRepository.LogAsync(new AuditLog
                {
                    UserId = userId,
                    Action = AuditAction.EmailBulkSend,
                    EntityType = "BulkEmail",
                    EntityId = userId,
                    Timestamp = DateTime.UtcNow,
                    IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    NewValues = JsonSerializer.Serialize(new
                    {
                        Subject = request.Subject,
                        SentBy = new
                        {
                            UserId = userId,
                            Name = $"{currentUser.FirstName} {currentUser.LastName}".Trim(),
                            EmailMasked = EmailAddressHelper.Mask(currentUser.Email)
                        },
                        ReplyTo = new
                        {
                            Name = replyToName,
                            EmailMasked = EmailAddressHelper.Mask(replyToEmail)
                        },
                        RecipientCount = recipientEmails.Count,
                        Recipients = recipientAudit
                    }),
                    Notes = "Bulk email sent"
                }, cancellationToken);

                _logger.LogInformation("Bulk email sent successfully to {RecipientCount} recipients by user {UserId}",
                    recipientEmails.Count, userId);

                var recipientDisplayItems = resolution.RecipientDisplayItems;
                if (recipientDisplayItems.Count == 0)
                {
                    recipientDisplayItems = await _recipientResolutionService.ResolveDisplayItemsByEmailAsync(
                        recipientEmails,
                        cancellationToken);
                }

                if (recipientDisplayItems.Count == 0)
                {
                    recipientDisplayItems = Enumerable
                        .Repeat("Unbekannter Kontakt", recipientEmails.Count)
                        .ToList();
                }

                var summarySent = await _emailService.SendForwardingSuccessSummaryAsync(
                    senderEmail: currentUser.Email,
                    forwardedRecipientDisplayItems: recipientDisplayItems,
                    droppedRecipientDisplayItems: Array.Empty<string>(),
                    originalSubject: request.Subject,
                    originalFromAddress: replyToEmail,
                    originalFromName: replyToName,
                    originalHtmlBody: sanitizedHtmlBody,
                    originalTextBody: sanitizedPlainTextBody,
                    cancellationToken: cancellationToken);

                if (!summarySent)
                {
                    _logger.LogWarning(
                        "Bulk email summary notification could not be sent to sender {UserId}",
                        userId);
                }

                return Ok(ApiResponse<SendBulkEmailResponse>.CreateSuccess(response));
            }
            else
            {
                _logger.LogWarning("Failed to send bulk email for user {UserId}", userId);
                return Ok(ApiResponse<SendBulkEmailResponse>.CreateSuccess(response,
                    "E-Mail konnte nicht vollständig gesendet werden."));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending bulk email for user {UserId}", GetUserId());
            return StatusCode(500, ApiResponse<SendBulkEmailResponse>.CreateError(
                "Fehler beim Senden der E-Mail.",
                "INTERNAL_ERROR"));
        }
    }

    private static string HtmlToPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var withLineBreaks = System.Text.RegularExpressions.Regex.Replace(
            html,
            @"</?(?:p|br|li|h[1-3]|blockquote)[^>]*>",
            Environment.NewLine,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var withoutTags = System.Text.RegularExpressions.Regex.Replace(withLineBreaks, "<[^>]+>", string.Empty);
        return System.Net.WebUtility.HtmlDecode(withoutTags).Trim();
    }

    /// <summary>
    /// Gets the user ID from the current claims principal.
    /// </summary>
    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
    }
}
