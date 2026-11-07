namespace Igevo.Vernetzt.Core.Enums;

/// <summary>
/// Preferred way a user wants to sign in. Chosen during onboarding.
/// </summary>
public enum LoginMethod
{
    /// <summary>
    /// One-time login link sent by email.
    /// </summary>
    EmailLink = 0,

    /// <summary>
    /// Passkey (fingerprint, face, device PIN).
    /// </summary>
    Passkey = 1
}
