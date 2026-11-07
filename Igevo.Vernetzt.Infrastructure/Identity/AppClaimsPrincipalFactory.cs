using System.Security.Claims;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Igevo.Vernetzt.Infrastructure.Identity;

/// <summary>
/// Builds the cookie principal from the Identity account plus the domain user's profile and roles.
/// Roles are stored on the domain user (<c>User.Claims</c>) and emitted as <see cref="ClaimTypes.Role"/>.
/// Because the security stamp validator re-creates the principal periodically, role changes are
/// picked up without forcing the user to log in again.
/// </summary>
public class AppClaimsPrincipalFactory : UserClaimsPrincipalFactory<AppIdentityUser>
{
    private readonly IUserRepository _userRepository;

    public AppClaimsPrincipalFactory(
        UserManager<AppIdentityUser> userManager,
        IOptions<IdentityOptions> optionsAccessor,
        IUserRepository userRepository)
        : base(userManager, optionsAccessor)
    {
        _userRepository = userRepository;
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppIdentityUser account)
    {
        var identity = await base.GenerateClaimsAsync(account);

        // The Identity user name is a technical value; replace name/email with the domain profile.
        foreach (var claim in identity.FindAll(c => c.Type is ClaimTypes.Name or ClaimTypes.Email).ToList())
        {
            identity.RemoveClaim(claim);
        }

        var user = await _userRepository.GetByIdAsync(account.Id);
        if (user == null)
        {
            return identity;
        }

        identity.AddClaim(new Claim(ClaimTypes.Email, user.Email));
        identity.AddClaim(new Claim(ClaimTypes.Name, user.FullName));
        identity.AddClaim(new Claim(ClaimTypes.GivenName, user.FirstName));
        identity.AddClaim(new Claim(ClaimTypes.Surname, user.LastName));
        identity.AddClaim(new Claim("email_verified", user.EmailVerified.ToString().ToLowerInvariant()));

        // Only roles go into the cookie. Club/department access is checked server-side
        // via IAuthorizationService so that permission changes apply immediately.
        foreach (var userClaim in user.Claims)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, userClaim.ToString()));
            identity.AddClaim(new Claim("user_claim", userClaim.ToString()));
        }

        return identity;
    }
}
