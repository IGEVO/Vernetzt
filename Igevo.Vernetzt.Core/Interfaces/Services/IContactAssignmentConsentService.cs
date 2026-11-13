using Igevo.Vernetzt.Core.DTOs.Users;
using Igevo.Vernetzt.Core.Enums;

namespace Igevo.Vernetzt.Core.Interfaces.Services;

/// <summary>
/// Consent of assigned users to their contact assignments. A new assignment stays pending (only visible
/// internally, no rights, no mails) until the user confirms it and chooses who may see it.
/// </summary>
public interface IContactAssignmentConsentService
{
    /// <summary>
    /// The assigned user consents to a pending assignment and sets its visibility.
    /// </summary>
    Task<ContactAssignmentConsentResult> ConfirmAsync(
        Guid userId, Guid assignmentId, VisibilityLevel visibility, CancellationToken cancellationToken = default);

    /// <summary>
    /// The assigned user declines a pending assignment; it is removed and the assigning person informed.
    /// </summary>
    Task<ContactAssignmentConsentResult> DeclineAsync(
        Guid userId, Guid assignmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// An editor of the entity sends the consent request again (rate-limited).
    /// </summary>
    Task<ContactAssignmentConsentResult> SendReminderAsync(
        Guid requestingUserId, Guid assignmentId, CancellationToken cancellationToken = default);
}
