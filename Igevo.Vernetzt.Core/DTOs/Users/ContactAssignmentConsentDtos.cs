namespace Igevo.Vernetzt.Core.DTOs.Users;

/// <summary>
/// Outcome of a consent action on a contact assignment (confirm, decline, reminder).
/// </summary>
public enum ContactAssignmentConsentStatus
{
    Success,
    NotFound,
    Forbidden,
    /// <summary>The assignment is not in the required state (e.g. already confirmed).</summary>
    Conflict,
    /// <summary>A reminder was sent recently.</summary>
    TooManyRequests
}

/// <summary>
/// Result of a consent action; <see cref="Message"/> is user-facing (German).
/// </summary>
public record ContactAssignmentConsentResult(ContactAssignmentConsentStatus Status, string Message)
{
    public bool Success => Status == ContactAssignmentConsentStatus.Success;
}
