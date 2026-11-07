using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Igevo.Vernetzt.Server.Identity;

/// <summary>
/// HTTP Basic authentication for the CardDAV endpoints: username = email address, password = the
/// personal CardDAV app password. Only used by endpoints that request this scheme explicitly; the
/// cookie/passkey session of the web app is not affected.
/// </summary>
public sealed class CardDavBasicAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "CardDav";
    public const string AuthenticationMethod = "carddav";

    /// <summary>Failed attempts per client IP within <see cref="FailureWindow"/> before requests are rejected.</summary>
    internal const int MaxFailuresPerWindow = 10;
    internal static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(5);

    private const string LockedOutItemKey = "CardDav.LockedOut";

    private readonly ICardDavService _cardDavService;
    private readonly IMemoryCache _cache;

    public CardDavBasicAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ICardDavService cardDavService,
        IMemoryCache cache)
        : base(options, logger, encoder)
    {
        _cardDavService = cardDavService;
        _cache = cache;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var header) ||
            !string.Equals(header.Scheme, "Basic", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(header.Parameter))
        {
            return AuthenticateResult.NoResult();
        }

        var failureKey = $"carddav-auth-failures:{Context.Connection.RemoteIpAddress}";
        if (_cache.TryGetValue(failureKey, out FailureCounter? counter) && counter!.Count >= MaxFailuresPerWindow)
        {
            Context.Items[LockedOutItemKey] = true;
            return AuthenticateResult.Fail("Too many failed CardDAV logins.");
        }

        string username;
        string password;
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter));
            var separator = decoded.IndexOf(':');
            if (separator <= 0)
            {
                return RegisterFailure(failureKey, "Malformed Basic credentials.");
            }

            username = decoded[..separator];
            password = decoded[(separator + 1)..];
        }
        catch (FormatException)
        {
            return RegisterFailure(failureKey, "Malformed Basic credentials.");
        }

        var userId = await _cardDavService.AuthenticateAsync(username, password, Context.RequestAborted);
        if (userId is null)
        {
            Logger.LogInformation("Failed CardDAV login from {RemoteIp}", Context.Connection.RemoteIpAddress);
            return RegisterFailure(failureKey, "Invalid CardDAV credentials.");
        }

        // A successful login clears earlier typos, so clients behind the same IP do not lock each other out.
        _cache.Remove(failureKey);

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()),
            new Claim(ClaimTypes.AuthenticationMethod, AuthenticationMethod)
        ], SchemeName);

        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (Context.Items.ContainsKey(LockedOutItemKey))
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            Response.Headers.RetryAfter = ((int)FailureWindow.TotalSeconds).ToString();
            return Task.CompletedTask;
        }

        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Basic realm=\"Vernetzt CardDAV\", charset=\"UTF-8\"";
        return Task.CompletedTask;
    }

    private AuthenticateResult RegisterFailure(string failureKey, string message)
    {
        var counter = _cache.GetOrCreate(failureKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = FailureWindow;
            return new FailureCounter();
        })!;
        Interlocked.Increment(ref counter.Count);
        return AuthenticateResult.Fail(message);
    }

    private sealed class FailureCounter
    {
        public int Count;
    }
}
