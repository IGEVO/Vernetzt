using Microsoft.Extensions.Configuration;

namespace Igevo.Vernetzt.Infrastructure.Identity;

/// <summary>
/// Shared constants for the login-link token provider (registered in the server host).
/// </summary>
public static class LoginLinkTokens
{
    /// <summary>
    /// Name under which the data-protection token provider for login links is registered.
    /// </summary>
    public const string ProviderName = "LoginLink";

    /// <summary>
    /// Token purpose; tokens for other purposes are not accepted.
    /// </summary>
    public const string Purpose = "login-link";

    /// <summary>
    /// Validity of a login link in minutes (<c>Auth:LoginLink:ExpirationMinutes</c>, default 15).
    /// </summary>
    public static int GetExpirationMinutes(IConfiguration configuration)
        => int.TryParse(configuration["Auth:LoginLink:ExpirationMinutes"], out var minutes) && minutes > 0 ? minutes : 15;
}
