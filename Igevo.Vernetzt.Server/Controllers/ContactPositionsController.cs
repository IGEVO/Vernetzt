using Igevo.Vernetzt.Core.DTOs;
using Igevo.Vernetzt.Core.DTOs.ContactPositions;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AuthService = Igevo.Vernetzt.Core.Interfaces.Services.IAuthorizationService;

namespace Igevo.Vernetzt.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ContactPositionsController : ControllerBase
{
    private readonly IContactPositionService _contactPositionService;
    private readonly AuthService _authorizationService;
    private readonly ILogger<ContactPositionsController> _logger;

    public ContactPositionsController(
        IContactPositionService contactPositionService,
        AuthService authorizationService,
        ILogger<ContactPositionsController> logger)
    {
        _contactPositionService = contactPositionService;
        _authorizationService = authorizationService;
        _logger = logger;
    }

    /// <summary>
    /// Get all contact positions (admin only)
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<ContactPositionDto>>>> GetAll(CancellationToken cancellationToken)
    {
        try
        {
            if (!await _authorizationService.IsManagerOrAdminAsync(cancellationToken))
            {
                return Forbid();
            }

            var positions = await _contactPositionService.GetAllAsync(cancellationToken);
            return Ok(ApiResponse<List<ContactPositionDto>>.CreateSuccess(positions));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all contact positions");
            return StatusCode(500, ApiResponse<object>.CreateError("Internal server error"));
        }
    }

    /// <summary>
    /// Get active contact positions (available to all authenticated users for selection).
    /// Returns same as GetAll since IsActive was removed.
    /// </summary>
    [HttpGet("active")]
    public async Task<ActionResult<ApiResponse<List<ContactPositionDto>>>> GetActive(CancellationToken cancellationToken)
    {
        try
        {
            var positions = await _contactPositionService.GetAllAsync(cancellationToken);
            return Ok(ApiResponse<List<ContactPositionDto>>.CreateSuccess(positions));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active contact positions");
            return StatusCode(500, ApiResponse<object>.CreateError("Internal server error"));
        }
    }

    /// <summary>
    /// Get contact position by ID (admin only)
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<ContactPositionDto>>> GetById(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            if (!await _authorizationService.IsManagerOrAdminAsync(cancellationToken))
            {
                return Forbid();
            }

            var position = await _contactPositionService.GetByIdAsync(id, cancellationToken);
            if (position == null)
            {
                return NotFound(ApiResponse<object>.CreateError("Contact position not found"));
            }

            return Ok(ApiResponse<ContactPositionDto>.CreateSuccess(position));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting contact position {PositionId}", id);
            return StatusCode(500, ApiResponse<object>.CreateError("Internal server error"));
        }
    }

    /// <summary>
    /// Create new contact position (admin only)
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<ContactPositionDto>>> Create(
        [FromBody] CreateContactPositionDto dto,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!await _authorizationService.IsManagerOrAdminAsync(cancellationToken))
            {
                return Forbid();
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ApiResponse<object>.CreateError("Invalid input"));
            }

            var created = await _contactPositionService.CreateAsync(dto, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, ApiResponse<ContactPositionDto>.CreateSuccess(created));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating contact position");
            return StatusCode(500, ApiResponse<object>.CreateError("Internal server error"));
        }
    }

    /// <summary>
    /// Update contact position (admin only)
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<ContactPositionDto>>> Update(
        Guid id,
        [FromBody] UpdateContactPositionDto dto,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!await _authorizationService.IsManagerOrAdminAsync(cancellationToken))
            {
                return Forbid();
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ApiResponse<object>.CreateError("Invalid input"));
            }

            var updated = await _contactPositionService.UpdateAsync(id, dto, cancellationToken);
            return Ok(ApiResponse<ContactPositionDto>.CreateSuccess(updated));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<object>.CreateError("Contact position not found"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating contact position {PositionId}", id);
            return StatusCode(500, ApiResponse<object>.CreateError("Internal server error"));
        }
    }

    /// <summary>
    /// Delete contact position (admin only)
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            if (!await _authorizationService.IsManagerOrAdminAsync(cancellationToken))
            {
                return Forbid();
            }

            var deleted = await _contactPositionService.DeleteAsync(id, cancellationToken);
            if (!deleted)
            {
                return NotFound(ApiResponse<object>.CreateError("Contact position not found"));
            }

            return Ok(ApiResponse<bool>.CreateSuccess(true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting contact position {PositionId}", id);
            return StatusCode(500, ApiResponse<object>.CreateError("Internal server error"));
        }
    }
}
