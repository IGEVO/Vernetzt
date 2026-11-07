using System.Security.Claims;
using Igevo.Vernetzt.Core.DTOs;
using Igevo.Vernetzt.Core.DTOs.CardDav;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Igevo.Vernetzt.Server.Controllers;

/// <summary>
/// Manages the current user's CardDAV access (app password and address book settings).
/// </summary>
[ApiController]
[Route("api/users/me/carddav")]
[Authorize]
[Produces("application/json")]
public class CardDavAccessController : ControllerBase
{
    private readonly ICardDavService _cardDavService;
    private readonly ILogger<CardDavAccessController> _logger;

    public CardDavAccessController(ICardDavService cardDavService, ILogger<CardDavAccessController> logger)
    {
        _cardDavService = cardDavService;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<CardDavStatusDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<CardDavStatusDto>>> GetStatus(CancellationToken cancellationToken = default)
    {
        var status = await _cardDavService.GetStatusAsync(GetUserId(), cancellationToken);
        return Ok(ApiResponse<CardDavStatusDto>.CreateSuccess(WithAbsoluteServerUrl(status)));
    }

    /// <summary>
    /// Creates a new app password (an existing one stops working). The password is only returned here.
    /// </summary>
    [HttpPost("token")]
    [ProducesResponseType(typeof(ApiResponse<CardDavAccessCreatedDto>), StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<CardDavAccessCreatedDto>>> CreateAccess(CancellationToken cancellationToken = default)
    {
        try
        {
            var created = await _cardDavService.CreateAccessAsync(GetUserId(), cancellationToken);
            created.Status = WithAbsoluteServerUrl(created.Status);
            return StatusCode(StatusCodes.Status201Created, ApiResponse<CardDavAccessCreatedDto>.CreateSuccess(created));
        }
        catch (Exception ex) when (ex is not UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Error creating CardDAV access");
            return StatusCode(500, ApiResponse<object>.CreateError("Der Zugang konnte nicht erstellt werden."));
        }
    }

    [HttpDelete("token")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeAccess(CancellationToken cancellationToken = default)
    {
        var revoked = await _cardDavService.RevokeAccessAsync(GetUserId(), cancellationToken: cancellationToken);
        return revoked
            ? NoContent()
            : NotFound(ApiResponse<object>.CreateError("Es gibt keinen aktiven Zugang.", "NOT_FOUND"));
    }

    [HttpPut("settings")]
    [ProducesResponseType(typeof(ApiResponse<CardDavStatusDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<CardDavStatusDto>>> UpdateSettings(
        [FromBody] UpdateCardDavSettingsDto dto,
        CancellationToken cancellationToken = default)
    {
        var status = await _cardDavService.UpdateSettingsAsync(GetUserId(), dto, cancellationToken);
        return Ok(ApiResponse<CardDavStatusDto>.CreateSuccess(WithAbsoluteServerUrl(status)));
    }

    /// <summary>
    /// Without App:BaseUrl the service only knows the path; complete it from the current request.
    /// </summary>
    private CardDavStatusDto WithAbsoluteServerUrl(CardDavStatusDto status)
    {
        if (status.ServerUrl.StartsWith('/'))
        {
            status.ServerUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}{status.ServerUrl}";
        }

        return status;
    }

    private Guid GetUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId)
            ? userId
            : throw new UnauthorizedAccessException("User ID not found in token");
    }
}
