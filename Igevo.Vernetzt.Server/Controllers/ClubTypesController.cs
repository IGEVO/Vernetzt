using System.Security.Claims;
using Igevo.Vernetzt.Core.DTOs;
using Igevo.Vernetzt.Core.DTOs.ClubTypes;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Igevo.Vernetzt.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ClubTypesController : ControllerBase
{
    private readonly IClubTypeService _clubTypeService;
    private readonly ILogger<ClubTypesController> _logger;

    public ClubTypesController(
        IClubTypeService clubTypeService,
        ILogger<ClubTypesController> logger)
    {
        _clubTypeService = clubTypeService;
        _logger = logger;
    }

    /// <summary>
    /// Gets all active club types.
    /// </summary>
    [HttpGet("active")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<ClubTypeDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IEnumerable<ClubTypeDto>>>> GetActiveClubTypes(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var clubTypes = await _clubTypeService.GetAllActiveAsync(cancellationToken);
            var dtos = clubTypes.Select(MapToDto);
            return Ok(ApiResponse<IEnumerable<ClubTypeDto>>.CreateSuccess(dtos));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active club types");
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim Abrufen der Vereinsarten aufgetreten."));
        }
    }

    /// <summary>
    /// Gets all club types (requires SystemAdmin).
    /// </summary>
    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<ClubTypeDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<IEnumerable<ClubTypeDto>>>> GetAllClubTypes(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = GetUserId();
            var clubTypes = await _clubTypeService.GetAllAsync(userId, cancellationToken);
            var dtos = clubTypes.Select(MapToDto);
            return Ok(ApiResponse<IEnumerable<ClubTypeDto>>.CreateSuccess(dtos));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized attempt to get all club types");
            return StatusCode(403, ApiResponse<object>.CreateError("Nur Systemadministratoren können alle Vereinsarten abrufen.", "FORBIDDEN"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all club types");
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim Abrufen der Vereinsarten aufgetreten."));
        }
    }

    /// <summary>
    /// Gets a specific club type by ID.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<ClubTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ClubTypeDto>>> GetClubType(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var clubType = await _clubTypeService.GetByIdAsync(id, cancellationToken);

            if (clubType == null)
            {
                return NotFound(ApiResponse<object>.CreateError("Vereinsart nicht gefunden.", "NOT_FOUND"));
            }

            return Ok(ApiResponse<ClubTypeDto>.CreateSuccess(MapToDto(clubType)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting club type {ClubTypeId}", id);
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim Abrufen der Vereinsart aufgetreten."));
        }
    }

    /// <summary>
    /// Creates a new club type (SystemAdmin only).
    /// </summary>
    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<ClubTypeDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<ClubTypeDto>>> CreateClubType(
        [FromBody] CreateClubTypeDto dto,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                return BadRequest(ApiResponse<object>.CreateError("Ungültige Eingabedaten.", "VALIDATION_ERROR", errors));
            }

            var userId = GetUserId();
            var clubType = MapFromCreateDto(dto);
            var created = await _clubTypeService.CreateAsync(userId, clubType, cancellationToken);

            return CreatedAtAction(
                nameof(GetClubType),
                new { id = created.Id },
                ApiResponse<ClubTypeDto>.CreateSuccess(MapToDto(created)));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized attempt to create club type");
            return StatusCode(403, ApiResponse<object>.CreateError("Nur Systemadministratoren können Vereinsarten erstellen.", "FORBIDDEN"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating club type");
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim Erstellen der Vereinsart aufgetreten."));
        }
    }

    /// <summary>
    /// Updates an existing club type (SystemAdmin only).
    /// </summary>
    [HttpPut("{id}")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<ClubTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<ClubTypeDto>>> UpdateClubType(
        Guid id,
        [FromBody] UpdateClubTypeDto dto,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                return BadRequest(ApiResponse<object>.CreateError("Ungültige Eingabedaten.", "VALIDATION_ERROR", errors));
            }

            var userId = GetUserId();
            var clubType = MapFromUpdateDto(dto);
            var updated = await _clubTypeService.UpdateAsync(userId, id, clubType, cancellationToken);

            if (updated == null)
            {
                return NotFound(ApiResponse<object>.CreateError("Vereinsart nicht gefunden oder keine Berechtigung.", "NOT_FOUND"));
            }

            return Ok(ApiResponse<ClubTypeDto>.CreateSuccess(MapToDto(updated)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating club type {ClubTypeId}", id);
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim Aktualisieren der Vereinsart aufgetreten."));
        }
    }

    /// <summary>
    /// Deletes a club type (SystemAdmin only).
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteClubType(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = GetUserId();
            var result = await _clubTypeService.DeleteAsync(userId, id, cancellationToken);

            if (!result)
            {
                return NotFound();
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting club type {ClubTypeId}", id);
            return StatusCode(500);
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

    private static ClubTypeDto MapToDto(ClubType clubType) => new()
    {
        Id = clubType.Id,
        Name = clubType.Name,
        Description = clubType.Description,
        CreatedAt = clubType.CreatedAt,
        UpdatedAt = clubType.UpdatedAt
    };

    private static ClubType MapFromCreateDto(CreateClubTypeDto dto) => new()
    {
        Name = dto.Name,
        Description = dto.Description,
    };

    private static ClubType MapFromUpdateDto(UpdateClubTypeDto dto) => new()
    {
        Name = dto.Name,
        Description = dto.Description,
    };
}
