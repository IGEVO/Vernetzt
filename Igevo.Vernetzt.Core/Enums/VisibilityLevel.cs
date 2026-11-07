namespace Igevo.Vernetzt.Core.Enums;

/// <summary>
/// Defines visibility levels for contact information and data.
/// For user fields (Email, Phone): Can be Internal, AllUsers, or Public.
/// For ContactPersonsVisibility: Only AllUsers and Public are used (all authenticated users can see all users).
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
