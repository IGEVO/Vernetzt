using Igevo.Vernetzt.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Igevo.Vernetzt.Server.Identity;

/// <summary>
/// Options for the login-link token provider (separate from the default data-protection token options
/// so that login links get their own, short lifespan).
/// </summary>
public sealed class LoginLinkTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public LoginLinkTokenProviderOptions()
    {
        Name = LoginLinkTokens.ProviderName;
        TokenLifespan = TimeSpan.FromMinutes(15);
    }
}

/// <summary>
/// Data-protection based token provider for one-time login links.
/// Tokens are bound to the user's security stamp, so "log out everywhere" also invalidates open links.
/// </summary>
public sealed class LoginLinkTokenProvider : DataProtectorTokenProvider<AppIdentityUser>
{
    public LoginLinkTokenProvider(
        IDataProtectionProvider dataProtectionProvider,
        IOptions<LoginLinkTokenProviderOptions> options,
        ILogger<DataProtectorTokenProvider<AppIdentityUser>> logger)
        : base(dataProtectionProvider, options, logger)
    {
    }
}
