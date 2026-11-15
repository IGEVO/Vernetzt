namespace Igevo.Vernetzt.Core.Enums;

/// <summary>
/// Defines visibility levels for contact information and data.
/// Used for user fields (Email, Phone) and per contact assignment (who may see the assignment and its
/// role/office address; chosen by the assigned user).
/// </summary>
public enum VisibilityLevel
{
    /// <summary>
    /// Only visible to system administrators and managers.
    /// Used for user email/phone privacy settings.
    /// </summary>
    Internal = 0,

    /// <summary>
    /// Visible to all authenticated users/clubs in the system.
    /// </summary>
    AllUsers = 1,

    /// <summary>
    /// Publicly visible (e.g., on public-facing pages).
    /// </summary>
    Public = 2
}
