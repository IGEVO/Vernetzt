using Igevo.Vernetzt.Core.DTOs.Auth;

namespace Igevo.Vernetzt.Core.Interfaces.Services;

/// <summary>
/// Application-level authentication operations around ASP.NET Core Identity.
/// Token generation/validation and session handling are done by the framework;
/// this service only adds the Vernetzt-specific rules (login-link flow, single use, audit, hints).
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Issues a time-limited login-link token for the user with the given email.
    /// Returns <c>null</c> if no matching user exists (the caller must not reveal this).
    /// </summary>
    Task<LoginLinkToken?> CreateLoginLinkTokenAsync(string email, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Issues a time-limited login-link token for a known user (e.g. right after onboarding).
    /// </summary>
    Task<LoginLinkToken?> CreateLoginLinkTokenForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a login-link token and marks it as used.
    /// Returns <c>true</c> only for a valid, unexpired and not yet used token.
    /// </summary>
    Task<bool> RedeemLoginLinkTokenAsync(Guid userId, string token, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends the login-link email.
    /// </summary>
    Task<bool> SendLoginLinkEmailAsync(string email, string loginLinkUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a successful login (last login timestamp + audit log).
    /// </summary>
    Task RecordLoginAsync(Guid userId, string method, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a logout in the audit log.
    /// </summary>
    Task RecordLogoutAsync(Guid userId, bool allDevices, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the Identity account that accompanies the domain user exists (created lazily for legacy data).
    /// </summary>
    Task EnsureIdentityAccountAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes all passkeys of another user and ends their sessions (SystemAdmin only),
    /// e.g. after a lost device. The user can then sign in with a login link and set up a new passkey.
    /// </summary>
    Task<PasskeyResetResult> ResetPasskeysAsync(Guid requestingUserId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Number of registered passkeys per user (users without passkeys are not contained).
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> GetPasskeyCountsAsync(CancellationToken cancellationToken = default);
}
