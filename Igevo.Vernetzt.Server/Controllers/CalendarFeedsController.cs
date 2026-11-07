using Igevo.Vernetzt.Core.DTOs;
using Igevo.Vernetzt.Core.DTOs.Events;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace Igevo.Vernetzt.Server.Controllers;

/// <summary>
/// Controller for tokenized calendar feed management and anonymous ICS access.
/// </summary>
[ApiController]
[Route("api/calendar-feeds")]
[Produces("application/json")]
public class CalendarFeedsController : ControllerBase
{
    private readonly ICalendarFeedService _calendarFeedService;
    private readonly ILogger<CalendarFeedsController> _logger;

    public CalendarFeedsController(ICalendarFeedService calendarFeedService, ILogger<CalendarFeedsController> logger)
    {
        _calendarFeedService = calendarFeedService;
        _logger = logger;
    }

    [HttpPost("tokens")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<CalendarFeedTokenCreatedDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<CalendarFeedTokenCreatedDto>>> CreateToken(
        [FromBody] CreateCalendarFeedTokenDto dto,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = GetUserId();
            var created = await _calendarFeedService.CreateTokenAsync(userId, dto, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, ApiResponse<CalendarFeedTokenCreatedDto>.CreateSuccess(created));
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(403, ApiResponse<object>.CreateError("Keine Berechtigung für diesen Feed.", "FORBIDDEN"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.CreateError(ex.Message, "INVALID_OPERATION"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating calendar feed token");
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim Erstellen des Feed-Tokens aufgetreten."));
        }
    }

    [HttpGet("tokens")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CalendarFeedTokenDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CalendarFeedTokenDto>>>> GetTokens(
        [FromQuery] Guid? clubId = null,
        [FromQuery] Guid? departmentId = null,
        [FromQuery] bool globalOnly = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!globalOnly && !clubId.HasValue && !departmentId.HasValue)
            {
                return BadRequest(ApiResponse<object>.CreateError("Bitte gib clubId, departmentId oder globalOnly=true an.", "VALIDATION_ERROR"));
            }

            var userId = GetUserId();
            var items = await _calendarFeedService.GetTokensAsync(userId, clubId, departmentId, globalOnly, cancellationToken);
            return Ok(ApiResponse<IReadOnlyList<CalendarFeedTokenDto>>.CreateSuccess(items));
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(403, ApiResponse<object>.CreateError("Keine Berechtigung für diesen Feed.", "FORBIDDEN"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.CreateError(ex.Message, "INVALID_OPERATION"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading calendar feed tokens");
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim Laden der Feed-Tokens aufgetreten."));
        }
    }

    [HttpPost("tokens/{tokenId:guid}/rotate")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<CalendarFeedTokenCreatedDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<CalendarFeedTokenCreatedDto>>> RotateToken(
        Guid tokenId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = GetUserId();
            var rotated = await _calendarFeedService.RotateTokenAsync(userId, tokenId, cancellationToken);
            if (rotated == null)
            {
                return NotFound(ApiResponse<object>.CreateError("Feed-Token nicht gefunden.", "NOT_FOUND"));
            }

            return Ok(ApiResponse<CalendarFeedTokenCreatedDto>.CreateSuccess(rotated));
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(403, ApiResponse<object>.CreateError("Keine Berechtigung für diesen Feed.", "FORBIDDEN"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rotating calendar feed token {TokenId}", tokenId);
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim Rotieren des Feed-Tokens aufgetreten."));
        }
    }

    [HttpDelete("tokens/{tokenId:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RevokeToken(Guid tokenId, CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = GetUserId();
            var revoked = await _calendarFeedService.RevokeTokenAsync(userId, tokenId, cancellationToken);
            if (!revoked)
            {
                return NotFound(ApiResponse<object>.CreateError("Feed-Token nicht gefunden.", "NOT_FOUND"));
            }

            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(403, ApiResponse<object>.CreateError("Keine Berechtigung für diesen Feed.", "FORBIDDEN"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking calendar feed token {TokenId}", tokenId);
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim Widerrufen des Feed-Tokens aufgetreten."));
        }
    }

    [HttpGet("{token}.ics")]
    [AllowAnonymous]
    [EnableRateLimiting("PublicTokenFlow")]
    [Produces("text/calendar")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetIcs(string token, CancellationToken cancellationToken = default)
    {
        try
        {
            var feed = await _calendarFeedService.GetFeedByTokenAsync(token, cancellationToken);
            if (feed == null)
            {
                return NotFound();
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(feed.Content);
            return File(bytes, "text/calendar; charset=utf-8", feed.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error serving calendar feed for token");
            return NotFound();
        }
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedAccessException("User ID not found in token");
        }

        return userId;
    }
}
