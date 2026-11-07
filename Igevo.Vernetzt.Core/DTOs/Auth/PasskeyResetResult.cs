namespace Igevo.Vernetzt.Core.DTOs.Auth;

/// <summary>
/// Outcome of an administrator resetting another user's passkeys.
/// </summary>
public enum PasskeyResetStatus
{
    Done,
    Forbidden,
    NotFound,
    OwnAccount
}

/// <summary>
/// Result of <c>IAuthService.ResetPasskeysAsync</c>.
/// </summary>
public sealed record PasskeyResetResult(PasskeyResetStatus Status, int RemovedPasskeys = 0);
