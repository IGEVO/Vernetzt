using System.Net;
using System.Security.Claims;
using Igevo.Vernetzt.Core.DTOs;
using Igevo.Vernetzt.Core.DTOs.Auth;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Identity;
using Igevo.Vernetzt.Infrastructure.Observability;
using Igevo.Vernetzt.Server.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;

namespace Igevo.Vernetzt.Server.Controllers;

/// <summary>
/// Authentication endpoints (login link, passkeys, session) on top of ASP.NET Core Identity.
/// The session is an HttpOnly Identity cookie; there are no tokens in the browser.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private const string GenericLoginLinkMessage = "Falls die E-Mail-Adresse bekannt ist, wurde ein Anmeldelink versendet.";

    private readonly IAuthService _authService;
    private readonly SignInManager<AppIdentityUser> _signInManager;
    private readonly UserManager<AppIdentityUser> _userManager;
    private readonly IUserRepository _userRepository;
    private readonly ICardDavService _cardDavService;
    private readonly ILogger<AuthController> _logger;
    private readonly VernetztMetrics _metrics;
    private readonly string _configuredBaseUrl;
    private readonly string? _publicWebUrl;

    public AuthController(
        IAuthService authService,
        SignInManager<AppIdentityUser> signInManager,
        UserManager<AppIdentityUser> userManager,
        IUserRepository userRepository,
        ICardDavService cardDavService,
        IConfiguration configuration,
        ILogger<AuthController> logger,
        VernetztMetrics metrics)
    {
        _cardDavService = cardDavService;
        _authService = authService;
        _signInManager = signInManager;
        _userManager = userManager;
        _userRepository = userRepository;
        _logger = logger;
        _metrics = metrics;
        _configuredBaseUrl = (configuration["App:BaseUrl"] ?? string.Empty).TrimEnd('/');
        _publicWebUrl = configuration["App:PublicWebUrl"]?.Trim().TrimEnd('/') is { Length: > 0 } url ? url : null;
    }

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>
    /// Sends a one-time login link to the given email address (if it belongs to a user).
    /// Always answers the same way so that registered addresses cannot be probed.
    /// </summary>
    [HttpPost("login-link/request")]
    [AllowAnonymous]
    [EnableRateLimiting("PublicAuth")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RequestLoginLink([FromBody] LoginLinkRequestDto request, CancellationToken cancellationToken)
    {
        try
        {
            var loginLink = await _authService.CreateLoginLinkTokenAsync(request.Email, ClientIp, cancellationToken);
            if (loginLink != null)
            {
                var returnUrl = NormalizeReturnUrl(request.ReturnUrl);
                if (!string.IsNullOrWhiteSpace(request.ReturnUrl) && returnUrl == null)
                {
                    _logger.LogWarning("Rejected unsafe returnUrl during login link request.");
                }

                var url = BuildLoginLinkUrl(loginLink, returnUrl);
                if (url == null)
                {
                    _logger.LogError("Login link could not be built because no trusted base URL is configured.");
                }
                else
                {
                    await _authService.SendLoginLinkEmailAsync(loginLink.Email, url, cancellationToken);
                }
            }

            return Ok(ApiResponse<object>.CreateSuccess(new { message = GenericLoginLinkMessage }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error requesting login link");
            return BadRequest(ApiResponse<object>.CreateError("Die Anfrage konnte nicht verarbeitet werden."));
        }
    }

    /// <summary>
    /// Redeems a login link and starts a session.
    /// </summary>
    [HttpPost("login-link/verify")]
    [AllowAnonymous]
    [EnableRateLimiting("PublicAuth")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> VerifyLoginLink([FromBody] VerifyLoginLinkDto request, CancellationToken cancellationToken)
    {
        if (!await _authService.RedeemLoginLinkTokenAsync(request.UserId, request.Token, ClientIp, cancellationToken))
        {
            return Unauthorized(ApiResponse<object>.CreateError("Der Anmeldelink ist ungültig, abgelaufen oder wurde bereits verwendet."));
        }

        var account = await _userManager.FindByIdAsync(request.UserId.ToString());
        if (account == null || !await _signInManager.CanSignInAsync(account))
        {
            return Unauthorized(ApiResponse<object>.CreateError("Anmeldung nicht möglich."));
        }

        await _signInManager.SignInAsync(account, isPersistent: true, authenticationMethod: PasskeyPolicy.LoginLinkAuthenticationMethod);
        await _authService.RecordLoginAsync(account.Id, "LoginLink", ClientIp, cancellationToken);
        return Ok(ApiResponse<object>.CreateSuccess(new { message = "Angemeldet." }));
    }

    /// <summary>
    /// Returns whether the current request carries a valid session (never 401).
    /// </summary>
    [HttpGet("status")]
    [AllowAnonymous]
    [EnableRateLimiting("PublicAuth")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckAuthStatus([FromServices] PasskeyPolicy passkeyPolicy, CancellationToken cancellationToken)
    {
        var isAuthenticated = User.Identity?.IsAuthenticated == true;
        var passkeyState = isAuthenticated ? await passkeyPolicy.EvaluateAsync(HttpContext, cancellationToken) : null;
        return Ok(ApiResponse<object>.CreateSuccess(new
        {
            isAuthenticated,
            userId = isAuthenticated ? User.FindFirstValue(ClaimTypes.NameIdentifier) : null,
            // The user's role requires a passkey session, but this session was started with a login link.
            passkeyRequired = passkeyState?.IsRestricted ?? false,
            hasPasskey = passkeyState?.HasPasskey ?? false,
            // Base URL of the public portal, used for "public page" links in the admin UI; null if not configured.
            publicWebUrl = _publicWebUrl
        }));
    }

    /// <summary>
    /// Creates WebAuthn creation options for the signed-in user.
    /// </summary>
    [HttpPost("passkeys/creation-options")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreatePasskeyCreationOptions(CancellationToken cancellationToken)
    {
        var account = await GetCurrentAccountAsync(cancellationToken);
        if (account == null)
        {
            return Unauthorized(ApiResponse<object>.CreateError("Nicht angemeldet."));
        }

        var user = await _userRepository.GetByIdAsync(account.Id, cancellationToken);
        var userEntity = new PasskeyUserEntity
        {
            Id = await _userManager.GetUserIdAsync(account),
            Name = user?.Email ?? account.Email ?? account.Id.ToString(),
            DisplayName = user?.FullName ?? account.Email ?? account.Id.ToString()
        };

        var optionsJson = await _signInManager.MakePasskeyCreationOptionsAsync(userEntity);
        return Content(optionsJson, "application/json");
    }

    /// <summary>
    /// Completes passkey registration for the signed-in user.
    /// </summary>
    [HttpPost("passkeys/attestation")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<PasskeyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CompletePasskeyAttestation([FromBody] PasskeyAttestationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CredentialJson))
        {
            return BadRequest(ApiResponse<object>.CreateError("CredentialJson is required."));
        }

        var account = await GetCurrentAccountAsync(cancellationToken);
        if (account == null)
        {
            return Unauthorized(ApiResponse<object>.CreateError("Nicht angemeldet."));
        }

        var result = await _signInManager.PerformPasskeyAttestationAsync(request.CredentialJson);
        if (!result.Succeeded)
        {
            _logger.LogWarning(result.Failure, "Passkey registration failed for user {UserId}", account.Id);
            _metrics.PasskeyFailed("registration");
            return BadRequest(ApiResponse<object>.CreateError("Der Passkey konnte nicht eingerichtet werden.", details: DevDetails(result.Failure)));
        }

        // The attestation must belong to the signed-in user.
        if (!string.Equals(result.UserEntity.Id, await _userManager.GetUserIdAsync(account), StringComparison.Ordinal))
        {
            _logger.LogWarning("Passkey attestation user mismatch for user {UserId}", account.Id);
            _metrics.PasskeyFailed("registration");
            return BadRequest(ApiResponse<object>.CreateError("Der Passkey konnte nicht eingerichtet werden."));
        }

        var passkey = result.Passkey;
        passkey.Name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim()[..Math.Min(request.Name.Trim().Length, 128)];
        var addResult = await _userManager.AddOrUpdatePasskeyAsync(account, passkey);
        if (!addResult.Succeeded)
        {
            return BadRequest(ApiResponse<object>.CreateError("Der Passkey konnte nicht gespeichert werden."));
        }

        _logger.LogInformation("Passkey registered for user {UserId}", account.Id);
        return Ok(ApiResponse<PasskeyDto>.CreateSuccess(PasskeyDto.From(passkey)));
    }

    /// <summary>
    /// Creates discoverable passkey request options for sign-in.
    /// </summary>
    [HttpPost("passkeys/request-options")]
    [AllowAnonymous]
    [EnableRateLimiting("PublicAuth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreatePasskeyRequestOptions()
    {
        var optionsJson = await _signInManager.MakePasskeyRequestOptionsAsync(user: null);
        return Content(optionsJson, "application/json");
    }

    /// <summary>
    /// Signs in with a passkey and starts a session.
    /// </summary>
    [HttpPost("passkeys/signin")]
    [AllowAnonymous]
    [EnableRateLimiting("PublicAuth")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SignInWithPasskey([FromBody] PasskeySignInRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CredentialJson))
        {
            return BadRequest(ApiResponse<object>.CreateError("CredentialJson is required."));
        }

        var assertion = await _signInManager.PerformPasskeyAssertionAsync(request.CredentialJson);
        if (!assertion.Succeeded)
        {
            _logger.LogWarning(assertion.Failure, "Passkey sign-in failed");
            _metrics.PasskeyFailed("signin");
            return Unauthorized(ApiResponse<object>.CreateError("Anmeldung mit Passkey fehlgeschlagen.", details: DevDetails(assertion.Failure)));
        }

        var account = assertion.User;
        if (!await _signInManager.CanSignInAsync(account))
        {
            return Unauthorized(ApiResponse<object>.CreateError("Anmeldung nicht möglich."));
        }

        // Persist the updated signature counter / backup flags (same as SignInManager.PasskeySignInAsync).
        await _userManager.AddOrUpdatePasskeyAsync(account, assertion.Passkey);
        await _signInManager.SignInAsync(account, isPersistent: true, authenticationMethod: PasskeyPolicy.PasskeyAuthenticationMethod);
        await _authService.RecordLoginAsync(account.Id, "Passkey", ClientIp, cancellationToken);
        return Ok(ApiResponse<object>.CreateSuccess(new { message = "Angemeldet." }));
    }

    /// <summary>
    /// Lists the signed-in user's passkeys.
    /// </summary>
    [HttpGet("passkeys")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<IList<PasskeyDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyPasskeys(CancellationToken cancellationToken)
    {
        var account = await GetCurrentAccountAsync(cancellationToken);
        if (account == null)
        {
            return Unauthorized(ApiResponse<object>.CreateError("Nicht angemeldet."));
        }

        var passkeys = await _userManager.GetPasskeysAsync(account);
        IList<PasskeyDto> result = passkeys
            .OrderBy(p => p.CreatedAt)
            .Select(PasskeyDto.From)
            .ToList();
        return Ok(ApiResponse<IList<PasskeyDto>>.CreateSuccess(result));
    }

    /// <summary>
    /// Deletes one of the signed-in user's passkeys.
    /// </summary>
    [HttpDelete("passkeys/{credentialIdBase64Url}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeletePasskey([FromRoute] string credentialIdBase64Url, CancellationToken cancellationToken)
    {
        byte[] credentialId;
        try
        {
            credentialId = WebEncoders.Base64UrlDecode(credentialIdBase64Url);
        }
        catch (FormatException)
        {
            return BadRequest(ApiResponse<object>.CreateError("Credential ID has an invalid format."));
        }

        var account = await GetCurrentAccountAsync(cancellationToken);
        if (account == null)
        {
            return Unauthorized(ApiResponse<object>.CreateError("Nicht angemeldet."));
        }

        var result = await _userManager.RemovePasskeyAsync(account, credentialId);
        if (!result.Succeeded)
        {
            return BadRequest(ApiResponse<object>.CreateError("Der Passkey konnte nicht entfernt werden."));
        }

        return NoContent();
    }

    /// <summary>
    /// Ends the current session.
    /// </summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            await _authService.RecordLogoutAsync(userId, allDevices: false, cancellationToken);
        }

        await _signInManager.SignOutAsync();
        return Ok(ApiResponse<object>.CreateSuccess(new { message = "Abgemeldet." }));
    }

    /// <summary>
    /// Ends all sessions of the current user on all devices (rotates the security stamp).
    /// Other devices are signed out at the next session validation (a few minutes at most).
    /// The CardDAV app password is revoked as well: the typical reason is a lost device, which is
    /// usually also the one syncing the address book.
    /// </summary>
    [HttpPost("logout-all")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> LogoutAll(CancellationToken cancellationToken)
    {
        var account = await GetCurrentAccountAsync(cancellationToken);
        if (account == null)
        {
            return Unauthorized(ApiResponse<object>.CreateError("Nicht angemeldet."));
        }

        await _userManager.UpdateSecurityStampAsync(account);
        await _cardDavService.RevokeAccessAsync(account.Id, "logout on all devices", cancellationToken);
        await _authService.RecordLogoutAsync(account.Id, allDevices: true, cancellationToken);
        await _signInManager.SignOutAsync();
        return Ok(ApiResponse<object>.CreateSuccess(new { message = "Auf allen Geräten abgemeldet." }));
    }

    private async Task<AppIdentityUser?> GetCurrentAccountAsync(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return null;
        }

        await _authService.EnsureIdentityAccountAsync(userId, cancellationToken);
        return await _userManager.FindByIdAsync(userId.ToString());
    }

    private List<string>? DevDetails(Exception? ex)
    {
        if (ex == null || !HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment())
        {
            return null;
        }

        var details = new List<string>();
        for (var current = ex; current != null && details.Count < 5; current = current.InnerException)
        {
            details.Add(current.Message);
        }

        return details;
    }

    private string? BuildLoginLinkUrl(LoginLinkToken loginLink, string? returnUrl)
    {
        var baseUri = ResolvePublicBaseUri();
        if (baseUri == null)
        {
            return null;
        }

        var query = new Dictionary<string, string?>
        {
            ["uid"] = loginLink.UserId.ToString(),
            ["token"] = loginLink.Token
        };
        if (!string.IsNullOrWhiteSpace(returnUrl))
        {
            query["returnUrl"] = returnUrl;
        }

        return QueryHelpers.AddQueryString(new Uri(baseUri, "/login").ToString(), query);
    }

    private Uri? ResolvePublicBaseUri()
    {
        if (Uri.TryCreate(_configuredBaseUrl, UriKind.Absolute, out var configuredBaseUri) &&
            (configuredBaseUri.Scheme == Uri.UriSchemeHttps || configuredBaseUri.Scheme == Uri.UriSchemeHttp))
        {
            return configuredBaseUri;
        }

        // Without a configured base URL only local development hosts are trusted.
        if (!IsLoopbackHost(Request.Host.Host))
        {
            return null;
        }

        return Uri.TryCreate($"{Request.Scheme}://{Request.Host.Value}", UriKind.Absolute, out var fallback) ? fallback : null;
    }

    internal static string? NormalizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return null;
        }

        var candidate = returnUrl.Trim();
        if (!candidate.StartsWith('/') || candidate.StartsWith("//", StringComparison.Ordinal) || candidate.StartsWith("/\\", StringComparison.Ordinal))
        {
            return null;
        }

        return Uri.TryCreate(candidate, UriKind.Relative, out _) ? candidate : null;
    }

    private static bool IsLoopbackHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(host, out var ipAddress) && IPAddress.IsLoopback(ipAddress));
    }

    public sealed class PasskeyAttestationRequest
    {
        public string CredentialJson { get; set; } = string.Empty;
        public string? Name { get; set; }
    }

    public sealed class PasskeySignInRequest
    {
        public string CredentialJson { get; set; } = string.Empty;
    }

    /// <summary>
    /// Passkey as shown in the personal settings.
    /// </summary>
    public sealed class PasskeyDto
    {
        public string CredentialId { get; set; } = string.Empty;
        public string? Name { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public bool IsBackedUp { get; set; }

        public static PasskeyDto From(UserPasskeyInfo passkey) => new()
        {
            CredentialId = WebEncoders.Base64UrlEncode(passkey.CredentialId),
            Name = passkey.Name,
            CreatedAt = passkey.CreatedAt,
            IsBackedUp = passkey.IsBackedUp
        };
    }
}
