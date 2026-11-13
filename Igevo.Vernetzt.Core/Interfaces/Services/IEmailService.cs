namespace Igevo.Vernetzt.Core.Interfaces.Services;

/// <summary>
/// Service for sending emails via SMTP.
/// Provides methods for sending various types of emails with privacy-compliant logging.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends an email to a single recipient.
    /// </summary>
    /// <param name="to">Recipient email address</param>
    /// <param name="subject">Email subject</param>
    /// <param name="htmlBody">HTML body content</param>
    /// <param name="plainTextBody">Plain text fallback (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if email was sent successfully</returns>
    Task<bool> SendEmailAsync(
        string to,
        string subject,
        string htmlBody,
        string? plainTextBody = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an email to multiple recipients.
    /// </summary>
    /// <param name="recipients">List of recipient email addresses</param>
    /// <param name="subject">Email subject</param>
    /// <param name="htmlBody">HTML body content</param>
    /// <param name="plainTextBody">Plain text fallback (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of successfully sent emails</returns>
    Task<int> SendBulkEmailAsync(
        IEnumerable<string> recipients,
        string subject,
        string htmlBody,
        string? plainTextBody = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an email to multiple recipients using BCC (all recipients hidden from each other).
    /// This is the preferred method for bulk emails to protect recipient privacy.
    /// </summary>
    /// <param name="recipients">List of recipient email addresses (will be added as BCC)</param>
    /// <param name="subject">Email subject</param>
    /// <param name="htmlBody">HTML body content</param>
    /// <param name="plainTextBody">Plain text fallback (optional)</param>
    /// <param name="replyToEmail">Reply-To email address (optional)</param>
    /// <param name="replyToName">Reply-To display name (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="fromEmail">Custom From address (optional, overrides system default)</param>
    /// <param name="fromName">Custom From display name (optional)</param>
    /// <param name="attachments">File attachments (optional)</param>
    /// <param name="toEmail">Explicit To address (optional, e.g. the sender for relay/bulk mails)</param>
    /// <param name="toName">Display name for the To address (optional)</param>
    /// <returns>True if email was sent successfully</returns>
    Task<bool> SendBccEmailAsync(
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
        string? toName = null);

    /// <summary>
    /// Sends a forwarding success confirmation to the sender after a BCC dispatch.
    /// Used by both relay and integrated bulk-email flows.
    /// </summary>
    /// <param name="senderEmail">Sender email receiving the confirmation</param>
    /// <param name="forwardedRecipientDisplayItems">Delivered recipients (display names only)</param>
    /// <param name="droppedRecipientDisplayItems">Recipients that were skipped (e.g. invalid addresses)</param>
    /// <param name="originalSubject">Original message subject</param>
    /// <param name="originalFromAddress">Original sender address</param>
    /// <param name="originalFromName">Original sender display name (optional)</param>
    /// <param name="originalHtmlBody">Original HTML body (optional)</param>
    /// <param name="originalTextBody">Original plain text body (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if confirmation email was sent successfully</returns>
    Task<bool> SendForwardingSuccessSummaryAsync(
        string senderEmail,
        IReadOnlyCollection<string> forwardedRecipientDisplayItems,
        IReadOnlyCollection<string> droppedRecipientDisplayItems,
        string originalSubject,
        string originalFromAddress,
        string? originalFromName,
        string? originalHtmlBody,
        string? originalTextBody,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a magic link authentication email.
    /// </summary>
    /// <param name="to">Recipient email address</param>
    /// <param name="magicLink">Magic link URL</param>
    /// <param name="expirationMinutes">Minutes until link expires</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if email was sent successfully</returns>
    Task<bool> SendMagicLinkEmailAsync(
        string to,
        string magicLink,
        int expirationMinutes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a notification email when a new user is created.
    /// </summary>
    /// <param name="to">New user's email address</param>
    /// <param name="userName">User's full name</param>
    /// <param name="loginUrl">URL to the login page (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if email was sent successfully</returns>
    Task<bool> SendUserCreatedNotificationAsync(
        string to,
        string userName,
        string? loginUrl = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks a user to consent to a new club assignment and to choose its visibility.
    /// </summary>
    /// <param name="to">User's email address</param>
    /// <param name="userName">User's full name</param>
    /// <param name="clubName">Club name</param>
    /// <param name="assignmentId">Assignment to confirm (deep link to "Mein Konto → Zuordnungen")</param>
    /// <param name="canEdit">Whether the assignment grants edit rights after consent</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if email was sent successfully</returns>
    Task<bool> SendClubAssignmentConsentRequestAsync(
        string to,
        string userName,
        string clubName,
        Guid assignmentId,
        bool canEdit = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks a user to consent to a new department/group assignment and to choose its visibility.
    /// </summary>
    /// <param name="to">User's email address</param>
    /// <param name="userName">User's full name</param>
    /// <param name="departmentName">Department name</param>
    /// <param name="clubName">Parent club name</param>
    /// <param name="assignmentId">Assignment to confirm (deep link to "Mein Konto → Zuordnungen")</param>
    /// <param name="canEdit">Whether the assignment grants edit rights after consent</param>
    /// <param name="parentDepartmentName">Parent department name if the assignment is to a group</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if email was sent successfully</returns>
    Task<bool> SendDepartmentAssignmentConsentRequestAsync(
        string to,
        string userName,
        string departmentName,
        string clubName,
        Guid assignmentId,
        bool canEdit = false,
        string? parentDepartmentName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Informs the person who created an assignment that the assigned user declined it.
    /// </summary>
    Task<bool> SendAssignmentDeclinedNotificationAsync(
        string to,
        string recipientName,
        string declinedByName,
        string organizationName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a test email to system admin on server startup.
    /// </summary>
    /// <param name="to">Admin email address</param>
    /// <param name="serverInfo">Server information (environment, version, etc.)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if email was sent successfully</returns>
    Task<bool> SendServerStartupNotificationAsync(
        string to,
        string serverInfo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an invitation email to a user with a link to complete onboarding.
    /// </summary>
    /// <param name="to">Recipient email address</param>
    /// <param name="invitedByName">Name of the user who sent the invitation</param>
    /// <param name="onboardingUrl">URL to the onboarding page with token</param>
    /// <param name="revokeUrl">URL to revoke/delete the invitation</param>
    /// <param name="expirationDays">Number of days until invitation expires</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if email was sent successfully</returns>
    Task<bool> SendInvitationEmailAsync(
        string to,
        string invitedByName,
        string onboardingUrl,
        string revokeUrl,
        int expirationDays,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a styled verification email for assignment context email confirmation.
    /// </summary>
    /// <param name="to">Recipient email address</param>
    /// <param name="verifyUrl">Verification URL with token</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if email was sent successfully</returns>
    Task<bool> SendContactAssignmentEmailVerificationAsync(
        string to,
        string verifyUrl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a hint to a role/office address (verified context email) that was used for a login attempt:
    /// signing in is only possible with the personal address of the account.
    /// </summary>
    /// <param name="to">The role/office address the login was requested for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if email was sent successfully</returns>
    Task<bool> SendContextEmailLoginHintAsync(
        string to,
        CancellationToken cancellationToken = default);
    /// <summary>
    /// Sends the confirmation link for a change of the personal (login) address to the new address.
    /// </summary>
    /// <param name="to">The new address</param>
    /// <param name="accountName">Full name of the account owner</param>
    /// <param name="confirmUrl">Confirmation URL with token</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if email was sent successfully</returns>
    Task<bool> SendAccountEmailChangeConfirmationAsync(
        string to,
        string accountName,
        string confirmUrl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the previous address that the login address of the account was changed.
    /// </summary>
    /// <param name="to">The previous address</param>
    /// <param name="accountName">Full name of the account owner</param>
    /// <param name="newEmailMasked">The new address, masked</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if email was sent successfully</returns>
    Task<bool> SendAccountEmailChangedNoticeAsync(
        string to,
        string accountName,
        string newEmailMasked,
        CancellationToken cancellationToken = default);
}
