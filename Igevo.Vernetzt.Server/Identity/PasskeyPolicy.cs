using System.Security.Claims;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Igevo.Vernetzt.Server.Identity;

/// <summary>
/// Configuration for roles that may only be exercised from a passkey session.
/// Section <c>Auth:PasskeyPolicy</c>.
/// </summary>
public sealed class PasskeyPolicyOptions
{
    public const string SectionName = "Auth:PasskeyPolicy";

    /// <summary>
    /// Roles whose holders need a passkey session for everything except passkey setup.
    /// <c>null</c> (not configured) means <see cref="UserClaim.SystemAdmin"/> and <see cref="UserClaim.Manager"/>;
    /// an empty list disables the policy.
    /// </summary>
    public List<UserClaim>? RequiredForRoles { get; set; }

    public IReadOnlyCollection<UserClaim> EffectiveRoles => RequiredForRoles ?? [UserClaim.SystemAdmin, UserClaim.Manager];
}

/// <summary>
/// Passkey situation of the current session.
/// </summary>
/// <param name="PasskeyRequired">The user holds a role that requires a passkey session.</param>
/// <param name="IsPasskeySession">The session was started with a passkey.</param>
/// <param name="HasPasskey">The user has at least one registered passkey.</param>
public sealed record PasskeySessionState(bool PasskeyRequired, bool IsPasskeySession, bool HasPasskey)
{
    /// <summary>
    /// The session may only use the passkey setup / sign-out endpoints.
    /// </summary>
    public bool IsRestricted => PasskeyRequired && !IsPasskeySession;

    /// <summary>
    /// A restricted session may only register the very first passkey (bootstrap);
    /// otherwise someone with access to the mailbox could add their own passkey.
    /// </summary>
    public bool CanRegisterPasskey => !IsRestricted || !HasPasskey;

    public static readonly PasskeySessionState Unrestricted = new(false, true, true);
}

/// <summary>
/// Decides whether a session must be upgraded to a passkey session.
/// Roles are read from the database (not from the cookie) so that a role granted
/// moments ago is enforced immediately, exactly like the domain authorization checks.
/// </summary>
public sealed class PasskeyPolicy
{
    public const string PasskeyAuthenticationMethod = "passkey";
    public const string LoginLinkAuthenticationMethod = "login-link";

    private static readonly object CacheKey = new();

    private readonly VernetztDbContext _dbContext;
    private readonly IOptionsMonitor<PasskeyPolicyOptions> _options;

    public PasskeyPolicy(VernetztDbContext dbContext, IOptionsMonitor<PasskeyPolicyOptions> options)
    {
        _dbContext = dbContext;
        _options = options;
    }

    public static bool IsPasskeySession(ClaimsPrincipal principal) =>
        principal.HasClaim(ClaimTypes.AuthenticationMethod, PasskeyAuthenticationMethod);

    /// <summary>
    /// Evaluates (and caches per request) the passkey state of the signed-in user.
    /// </summary>
    public async Task<PasskeySessionState> EvaluateAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        if (httpContext.Items.TryGetValue(CacheKey, out var cached) && cached is PasskeySessionState cachedState)
        {
            return cachedState;
        }

        var state = await EvaluateAsync(httpContext.User, cancellationToken);
        httpContext.Items[CacheKey] = state;
        return state;
    }

    public async Task<PasskeySessionState> EvaluateAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        if (principal.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return PasskeySessionState.Unrestricted;
        }

        var requiredRoles = _options.CurrentValue.EffectiveRoles;
        var isPasskeySession = IsPasskeySession(principal);

        var roles = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.Claims)
            .FirstOrDefaultAsync(cancellationToken) ?? [];
        var passkeyRequired = roles.Any(requiredRoles.Contains);

        var hasPasskey = await _dbContext.Set<IdentityUserPasskey<Guid>>()
            .AnyAsync(p => p.UserId == userId, cancellationToken);

        return new PasskeySessionState(passkeyRequired, isPasskeySession, hasPasskey);
    }

    /// <summary>
    /// The security stamp validator rebuilds the principal every few minutes and would drop the
    /// authentication method; keep it so a passkey session stays a passkey session.
    /// </summary>
    public static Task KeepAuthenticationMethod(SecurityStampRefreshingPrincipalContext context)
    {
        if (context.NewPrincipal?.Identity is ClaimsIdentity newIdentity && context.CurrentPrincipal != null)
        {
            foreach (var claim in context.CurrentPrincipal.FindAll(c => c.Type is ClaimTypes.AuthenticationMethod or "amr"))
            {
                if (!newIdentity.HasClaim(claim.Type, claim.Value))
                {
                    newIdentity.AddClaim(new Claim(claim.Type, claim.Value));
                }
            }
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Deny-by-default gate: a user whose role requires a passkey but whose session was started
/// with a login link can only reach the endpoints needed to set up a passkey and sign in with it.
/// Domain authorization checks read roles from the database, so restricting the session is the
/// only place that reliably covers all of them.
/// </summary>
public sealed class PasskeyPolicyMiddleware
{
    public const string ErrorCode = "PASSKEY_REQUIRED";

    private static readonly (string Method, string Path)[] AllowedWhileRestricted =
    [
        ("GET", "/api/auth/status"),
        ("POST", "/api/auth/logout"),
        ("POST", "/api/auth/logout-all"),
        ("GET", "/api/auth/passkeys"),
        ("POST", "/api/auth/passkeys/request-options"),
        ("POST", "/api/auth/passkeys/signin"),
        ("POST", "/api/auth/login-link/request"),
        ("POST", "/api/auth/login-link/verify"),
        ("GET", "/api/users/me")
    ];

    private static readonly (string Method, string Path)[] PasskeyRegistration =
    [
        ("POST", "/api/auth/passkeys/creation-options"),
        ("POST", "/api/auth/passkeys/attestation")
    ];

    private readonly RequestDelegate _next;

    public PasskeyPolicyMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, PasskeyPolicy policy)
    {
        if (context.User.Identity?.IsAuthenticated != true ||
            PasskeyPolicy.IsPasskeySession(context.User) ||
            !context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        var state = await policy.EvaluateAsync(context, context.RequestAborted);
        if (!state.IsRestricted || Matches(context, AllowedWhileRestricted) ||
            (state.CanRegisterPasskey && Matches(context, PasskeyRegistration)))
        {
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(Core.DTOs.ApiResponse<object>.CreateError(
            state.HasPasskey
                ? "Bitte melde dich mit deinem Passkey an, um diese Funktion zu nutzen."
                : "Bitte richte einen Passkey ein und melde dich damit an, um diese Funktion zu nutzen.",
            ErrorCode));
    }

    private static bool Matches(HttpContext context, IEnumerable<(string Method, string Path)> endpoints)
    {
        var path = context.Request.Path.Value?.TrimEnd('/') ?? string.Empty;
        return endpoints.Any(e =>
            HttpMethods.Equals(context.Request.Method, e.Method) &&
            string.Equals(path, e.Path, StringComparison.OrdinalIgnoreCase));
    }
}
