using System.ComponentModel.DataAnnotations;

namespace Igevo.Vernetzt.Core.DTOs.Auth;

/// <summary>
/// Request to send a one-time login link to the given email address.
/// </summary>
public class LoginLinkRequestDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Optional relative URL the user should land on after login.
    /// </summary>
    public string? ReturnUrl { get; set; }
}

/// <summary>
/// Request to redeem a one-time login link.
/// </summary>
public class VerifyLoginLinkDto
{
    [Required]
    public Guid UserId { get; set; }

    [Required]
    public string Token { get; set; } = string.Empty;
}

/// <summary>
/// A freshly issued login-link token for a user.
/// </summary>
/// <param name="UserId">The user the token belongs to.</param>
/// <param name="Email">The user's login email address.</param>
/// <param name="Token">The opaque, time-limited token (framework-protected).</param>
public sealed record LoginLinkToken(Guid UserId, string Email, string Token);
