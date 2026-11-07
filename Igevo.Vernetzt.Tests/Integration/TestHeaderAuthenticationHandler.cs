using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Igevo.Vernetzt.Tests.Integration;

internal sealed class TestHeaderAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    internal const string SchemeName = "TestHeaderAuth";
    internal const string UserIdHeaderName = "X-Test-UserId";
    /// <summary>Optional comma-separated role claims (cookie sessions get them from the domain user).</summary>
    internal const string RolesHeaderName = "X-Test-Roles";

    public TestHeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserIdHeaderName, out var headerValue))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var rawUserId = headerValue.FirstOrDefault();
        if (!Guid.TryParse(rawUserId, out var userId))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid X-Test-UserId header."));
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            // Header-authenticated test requests count as passkey sessions (see PasskeyPolicy).
            new(ClaimTypes.AuthenticationMethod, "passkey")
        };

        if (Request.Headers.TryGetValue(RolesHeaderName, out var roles))
        {
            claims.AddRange(roles.ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(role => new Claim(ClaimTypes.Role, role)));
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
