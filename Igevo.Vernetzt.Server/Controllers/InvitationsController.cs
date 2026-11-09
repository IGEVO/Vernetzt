using System.Security.Claims;
using Igevo.Vernetzt.Core.Configuration;
using Igevo.Vernetzt.Core.DTOs;
using Igevo.Vernetzt.Core.DTOs.Invitations;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Server.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Igevo.Vernetzt.Server.Controllers;

/// <summary>
/// Controller for user invitation and onboarding.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class InvitationsController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IAuthService _authService;
    private readonly AppSettings _appSettings;
    private readonly IOptionsMonitor<PasskeyPolicyOptions> _passkeyPolicyOptions;
    private readonly ILogger<InvitationsController> _logger;

    public InvitationsController(
        IUserService userService,
        IAuthService authService,
        IOptions<AppSettings> appSettings,
        IOptionsMonitor<PasskeyPolicyOptions> passkeyPolicyOptions,
        ILogger<InvitationsController> logger)
    {
        _userService = userService;
        _authService = authService;
        _appSettings = appSettings.Value;
        _passkeyPolicyOptions = passkeyPolicyOptions;
        _logger = logger;
    }

    private bool RequiresPasskey(IEnumerable<UserClaim> claims) =>
        claims.Any(_passkeyPolicyOptions.CurrentValue.EffectiveRoles.Contains);

    private Guid GetUserId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>
    /// Invite a user by email. Any authenticated user can invite others.
    /// </summary>
    [HttpPost("invite")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<InvitationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> InviteUser([FromBody] InviteUserDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var userId = GetUserId();
            var response = await _userService.InviteUserAsync(userId, dto, cancellationToken);
            return Ok(ApiResponse<InvitationResponse>.CreateSuccess(response));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized invitation attempt");
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending invitation");
            return BadRequest(ApiResponse<object>.CreateError("Fehler beim Versenden der Einladung."));
        }
    }

    /// <summary>
    /// Get invitation details by token for the onboarding page.
    /// </summary>
    [HttpGet("details")]
    [AllowAnonymous]
    [EnableRateLimiting("PublicTokenFlow")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInvitationDetails([FromQuery] string token, CancellationToken cancellationToken)
    {
        var details = await _userService.GetInvitationDetailsAsync(token, cancellationToken);
        if (details == null)
        {
            return NotFound(ApiResponse<object>.CreateError("Einladung nicht gefunden oder abgelaufen."));
        }

        details.PasskeyRequired = RequiresPasskey(details.PendingClaims);
        return Ok(ApiResponse<InvitationDetailsResponse>.CreateSuccess(details));
    }

    /// <summary>
    /// Accept an invitation and create user account.
    /// </summary>
    [HttpPost("accept")]
    [AllowAnonymous]
    [EnableRateLimiting("PublicTokenFlow")]
    [ProducesResponseType(typeof(ApiResponse<AcceptInvitationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AcceptInvitation([FromBody] AcceptInvitationDto dto, CancellationToken cancellationToken)
    {
        try
        {
            // Roles that require a passkey session must not choose the email link as login method
            if (dto.PreferredLoginMethod != LoginMethod.Passkey)
            {
                var details = await _userService.GetInvitationDetailsAsync(dto.Token, cancellationToken);
                if (details != null && RequiresPasskey(details.PendingClaims))
                {
                    dto.PreferredLoginMethod = LoginMethod.Passkey;
                }
            }

            var response = await _userService.AcceptInvitationAsync(dto, cancellationToken);
            if (!response.Success)
            {
                return BadRequest(ApiResponse<AcceptInvitationResponse>.CreateError(response.Message));
            }

            // No automatic sign-in: the long-lived invitation link only allows filling in the form.
            // A fresh, short-lived login link proves current access to the mailbox.
            try
            {
                var loginLink = await _authService.CreateLoginLinkTokenAsync(response.Email!, ClientIp, cancellationToken);
                if (loginLink != null)
                {
                    var returnUrl = dto.PreferredLoginMethod == LoginMethod.Passkey
                        ? "/willkommen?einrichten=passkey"
                        : "/willkommen";
                    var loginLinkUrl = QueryHelpers.AddQueryString($"{_appSettings.BaseUrl.TrimEnd('/')}/login", new Dictionary<string, string?>
                    {
                        ["uid"] = loginLink.UserId.ToString(),
                        ["token"] = loginLink.Token,
                        ["returnUrl"] = returnUrl
                    });

                    response.MagicLinkSent = await _authService.SendLoginLinkEmailAsync(loginLink.Email, loginLinkUrl, cancellationToken);
                    if (response.MagicLinkSent)
                    {
                        response.Message = "Fast geschafft! Wir haben dir einen Anmeldelink per E-Mail geschickt.";
                        _logger.LogInformation("Login link sent to user {UserId} after onboarding", loginLink.UserId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send login link after onboarding");
                // Don't fail the onboarding if sending the login link fails
            }

            // Don't include email in the response sent to the client
            response.Email = null;

            return Ok(ApiResponse<AcceptInvitationResponse>.CreateSuccess(response));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error accepting invitation");
            return BadRequest(ApiResponse<object>.CreateError("Fehler beim Annehmen der Einladung."));
        }
    }

    /// <summary>
    /// Revoke an invitation token. Callable by the invited user via a link in the email.
    /// Returns HTML since this is accessed directly from the browser via email link.
    /// </summary>
    [HttpGet("revoke")]
    [AllowAnonymous]
    [EnableRateLimiting("PublicTokenFlow")]
    public async Task<IActionResult> RevokeInvitation([FromQuery] string token, CancellationToken cancellationToken)
    {
        var success = await _userService.RevokeInvitationAsync(token, cancellationToken);

        var title = success
            ? "Einladung widerrufen"
            : "Einladung nicht gefunden";
        var message = success
            ? "Deine Einladung wurde erfolgreich widerrufen und deine Daten wurden gelöscht."
            : "Diese Einladung wurde nicht gefunden, ist bereits abgelaufen oder wurde bereits verwendet.";
        var iconColor = success ? "#4caf50" : "#f44336";
        var icon = success ? "&#10003;" : "&#10007;";

        var html = $@"<!DOCTYPE html>
<html lang=""de"">
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
    <title>{title} - Vernetzt</title>
    <style>
        body {{ font-family: Arial, sans-serif; background: #f5f5f5; margin: 0; padding: 40px 20px; color: #333; }}
        .card {{ max-width: 500px; margin: 0 auto; background: #fff; border-radius: 8px; box-shadow: 0 2px 8px rgba(0,0,0,.1); padding: 40px; text-align: center; }}
        .icon {{ font-size: 48px; color: {iconColor}; margin-bottom: 16px; }}
        h1 {{ color: #0D4A70; font-size: 24px; margin: 0 0 16px; }}
        p {{ color: #666; line-height: 1.6; }}
    </style>
</head>
<body>
    <div class=""card"">
        <div class=""icon"">{icon}</div>
        <h1>{title}</h1>
        <p>{message}</p>
    </div>
</body>
</html>";

        var statusCode = success ? 200 : 404;
        return new ContentResult
        {
            Content = html,
            ContentType = "text/html; charset=utf-8",
            StatusCode = statusCode
        };
    }
}
