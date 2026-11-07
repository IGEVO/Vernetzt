using System.Security.Claims;
using Igevo.Vernetzt.Core.DTOs;
using Igevo.Vernetzt.Core.DTOs.EmailRelay;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Igevo.Vernetzt.Server.Controllers;

/// <summary>
/// Controller for email relay operations — token-based email forwarding.
/// </summary>
[ApiController]
[Route("api/email/relay")]
[Authorize]
public class EmailRelayController : ControllerBase
{
    private readonly IEmailRelayService _relayService;
    private readonly ILogger<EmailRelayController> _logger;
    private readonly bool _isRelayEnabled;

    public EmailRelayController(
        IEmailRelayService relayService,
        IConfiguration configuration,
        ILogger<EmailRelayController> logger)
    {
        _relayService = relayService;
        _logger = logger;
        _isRelayEnabled = configuration.GetValue<bool>("EmailRelay:Enabled");
    }

    /// <summary>
    /// Returns whether the email relay feature is enabled.
    /// </summary>
    [HttpGet("status")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public IActionResult GetRelayStatus()
    {
        return Ok(ApiResponse<bool>.CreateSuccess(_isRelayEnabled));
    }

    /// <summary>
    /// Creates a new email relay token with a unique email address.
    /// </summary>
    [HttpPost("token")]
    [ProducesResponseType(typeof(ApiResponse<CreateEmailRelayTokenResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<CreateEmailRelayTokenResponse>>> CreateToken(
        [FromBody] CreateEmailRelayTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_isRelayEnabled)
        {
            return BadRequest(ApiResponse<CreateEmailRelayTokenResponse>.CreateError(
                "Die E-Mail-Relay-Funktion ist nicht aktiviert.", "RELAY_DISABLED"));
        }

        try
        {
            var userId = GetUserId();
            if (userId == Guid.Empty)
            {
                return Unauthorized(ApiResponse<CreateEmailRelayTokenResponse>.CreateError(
                    "Benutzer nicht authentifiziert.", "UNAUTHORIZED"));
            }

            var response = await _relayService.CreateTokenAsync(request, userId, cancellationToken);

            _logger.LogInformation(
                "Relay token created: TokenId={TokenId}, User={UserId}, Recipients={Count}",
                response.TokenId, userId, response.RecipientCount);

            return StatusCode(StatusCodes.Status201Created,
                ApiResponse<CreateEmailRelayTokenResponse>.CreateSuccess(response));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to create relay token for user {UserId}", GetUserId());
            return BadRequest(ApiResponse<CreateEmailRelayTokenResponse>.CreateError(
                ex.Message, "VALIDATION_ERROR"));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Forbidden recipient selection while creating relay token for user {UserId}", GetUserId());
            return StatusCode(403, ApiResponse<CreateEmailRelayTokenResponse>.CreateError(
                "Mindestens ein Empfänger ist nicht zugänglich.", "FORBIDDEN_RECIPIENT_ASSIGNMENT"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating relay token for user {UserId}", GetUserId());
            return StatusCode(500, ApiResponse<CreateEmailRelayTokenResponse>.CreateError(
                "Fehler beim Erstellen des Relay-Tokens.", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Gets all active (non-expired, non-used) relay tokens for the current user.
    /// </summary>
    [HttpGet("tokens")]
    [ProducesResponseType(typeof(ApiResponse<List<EmailRelayTokenDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<List<EmailRelayTokenDto>>>> GetActiveTokens(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = GetUserId();
            if (userId == Guid.Empty)
            {
                return Unauthorized(ApiResponse<List<EmailRelayTokenDto>>.CreateError(
                    "Benutzer nicht authentifiziert.", "UNAUTHORIZED"));
            }

            var tokens = await _relayService.GetActiveTokensAsync(userId, cancellationToken);
            return Ok(ApiResponse<List<EmailRelayTokenDto>>.CreateSuccess(tokens));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active tokens for user {UserId}", GetUserId());
            return StatusCode(500, ApiResponse<List<EmailRelayTokenDto>>.CreateError(
                "Fehler beim Laden der Tokens.", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Cancels (soft-deletes) a relay token. Only the owning user can cancel their token.
    /// </summary>
    [HttpDelete("token/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelToken(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = GetUserId();
            if (userId == Guid.Empty)
            {
                return Unauthorized();
            }

            var result = await _relayService.CancelTokenAsync(id, userId, cancellationToken);

            if (!result)
            {
                return NotFound(ApiResponse<object>.CreateError(
                    "Token nicht gefunden.", "NOT_FOUND"));
            }

            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<object>.CreateError(
                    "Sie können nur eigene Tokens löschen.", "FORBIDDEN"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling relay token {TokenId} for user {UserId}", id, GetUserId());
            return StatusCode(500, ApiResponse<object>.CreateError(
                "Fehler beim Löschen des Tokens.", "INTERNAL_ERROR"));
        }
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
    }
}
