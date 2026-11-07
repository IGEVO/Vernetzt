using Igevo.Vernetzt.Core.Entities;
using Microsoft.AspNetCore.Identity;

namespace Igevo.Vernetzt.Infrastructure.Identity;

/// <summary>
/// ASP.NET Core Identity account that accompanies a domain <see cref="User"/> 1:1 (same Id).
/// It only holds authentication data (security stamp, passkeys); profile and roles stay on <see cref="User"/>.
/// </summary>
public class AppIdentityUser : IdentityUser<Guid>
{
    /// <summary>
    /// Creates a new Identity account for the given domain user.
    /// The user name is derived from the immutable Id so it always passes Identity's user name validation.
    /// </summary>
    public static AppIdentityUser CreateFor(User user)
    {
        var account = new AppIdentityUser
        {
            Id = user.Id,
            UserName = user.Id.ToString("N"),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString()
        };
        account.NormalizedUserName = account.UserName.ToUpperInvariant();
        account.ApplyEmail(user.Email);
        return account;
    }

    /// <summary>
    /// Keeps the Identity account's email in sync with the domain user.
    /// </summary>
    public void ApplyEmail(string email)
    {
        Email = email;
        NormalizedEmail = email.ToUpperInvariant();
    }
}
