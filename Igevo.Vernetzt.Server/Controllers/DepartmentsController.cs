using System.Security.Claims;
using Igevo.Vernetzt.Core.DTOs;
using Igevo.Vernetzt.Core.DTOs.Common;
using Igevo.Vernetzt.Core.DTOs.Departments;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Exceptions;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Igevo.Vernetzt.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DepartmentsController : ControllerBase
{
    private readonly IDepartmentService _departmentService;
    private readonly IClubService _clubService;
    private readonly Core.Interfaces.Services.IAuthorizationService _authorizationService;
    private readonly DtoMappingService _dtoMappingService;
    private readonly ILogger<DepartmentsController> _logger;

    public DepartmentsController(
        IDepartmentService departmentService,
        IClubService clubService,
        Core.Interfaces.Services.IAuthorizationService authorizationService,
        DtoMappingService dtoMappingService,
        ILogger<DepartmentsController> logger)
    {
        _departmentService = departmentService;
        _clubService = clubService;
        _authorizationService = authorizationService;
        _dtoMappingService = dtoMappingService;
        _logger = logger;
    }

    /// <summary>
    /// Gets all departments accessible to the current user, optionally filtered by club.
    /// Groups are only returned with <paramref name="includeGroups"/>; they then follow their department.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<DepartmentDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<IEnumerable<DepartmentDto>>>> GetDepartments(
        [FromQuery] Guid? clubId = null,
        [FromQuery] bool includeGroups = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = GetUserId();

            IEnumerable<Department> departments;
            if (clubId.HasValue)
            {
                departments = await _departmentService.GetByClubIdAsync(userId, clubId.Value, cancellationToken);
            }
            else
            {
                departments = await _departmentService.GetAccessibleDepartmentsAsync(userId, cancellationToken);
            }

            var ordered = OrderHierarchically(departments, includeGroups);

            var dtos = new List<DepartmentDto>();
            foreach (var dept in ordered)
            {
                dtos.Add(await MapToDtoAsync(dept, userId, cancellationToken));
            }
            return Ok(ApiResponse<IEnumerable<DepartmentDto>>.CreateSuccess(dtos));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting departments");
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim Abrufen der Abteilungen aufgetreten."));
        }
    }

    /// <summary>
    /// Gets a specific department by ID.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<DepartmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<DepartmentDto>>> GetDepartment(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = GetUserId();
            var department = await _departmentService.GetByIdAsync(userId, id, cancellationToken);

            if (department == null)
            {
                return NotFound(ApiResponse<object>.CreateError("Abteilung nicht gefunden oder kein Zugriff.", "NOT_FOUND"));
            }

            return Ok(ApiResponse<DepartmentDto>.CreateSuccess(await MapToDtoAsync(department, userId, cancellationToken)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting department {DepartmentId}", id);
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim Abrufen der Abteilung aufgetreten."));
        }
    }

    /// <summary>
    /// Creates a new department.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<DepartmentDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<DepartmentDto>>> CreateDepartment(
        [FromBody] CreateDepartmentDto dto,
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
            var department = MapFromCreateDto(dto);
            var created = await _departmentService.CreateAsync(userId, department, cancellationToken, dto.LocationIds);

            return CreatedAtAction(
                nameof(GetDepartment),
                new { id = created.Id },
                ApiResponse<DepartmentDto>.CreateSuccess(await MapToDtoAsync(created, userId, cancellationToken)));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized attempt to create department");
            return StatusCode(403, ApiResponse<object>.CreateError("Du hast keine Berechtigung, Abteilungen für diesen Verein zu erstellen.", "FORBIDDEN"));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation when creating department");
            return BadRequest(ApiResponse<object>.CreateError(ex.Message, "INVALID_OPERATION"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating department");
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim Erstellen der Abteilung aufgetreten."));
        }
    }

    /// <summary>
    /// Updates an existing department.
    /// </summary>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(ApiResponse<DepartmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<DepartmentDto>>> UpdateDepartment(
        Guid id,
        [FromBody] UpdateDepartmentDto dto,
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
            var department = MapFromUpdateDto(dto);
            var updated = await _departmentService.UpdateAsync(userId, id, department, cancellationToken, dto.LocationIds);

            if (updated == null)
            {
                return NotFound(ApiResponse<object>.CreateError("Abteilung nicht gefunden oder kein Zugriff.", "NOT_FOUND"));
            }

            return Ok(ApiResponse<DepartmentDto>.CreateSuccess(await MapToDtoAsync(updated, userId, cancellationToken)));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized attempt to update department {DepartmentId}", id);
            return StatusCode(403, ApiResponse<object>.CreateError("Du hast keine Berechtigung, diese Abteilung zu bearbeiten.", "FORBIDDEN"));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation when updating department {DepartmentId}", id);
            return BadRequest(ApiResponse<object>.CreateError(ex.Message, "INVALID_OPERATION"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating department {DepartmentId}", id);
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim Aktualisieren der Abteilung aufgetreten."));
        }
    }

    /// <summary>
    /// Deletes a department.
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteDepartment(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = GetUserId();
            var result = await _departmentService.DeleteAsync(userId, id, cancellationToken);

            if (!result)
            {
                return NotFound();
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting department {DepartmentId}", id);
            return StatusCode(500);
        }
    }

    /// <summary>
    /// Adds a contact person to a department.
    /// </summary>
    [HttpPost("{id}/contact-persons/{contactPersonId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> AddContactPerson(
        Guid id,
        Guid contactPersonId,
        [FromQuery] Guid? positionId = null,
        [FromQuery] bool canEdit = false,
        [FromQuery] bool receivesLocationInfo = false,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] AssignContactPersonRequest? body = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = GetUserId();
            var notes = body?.Notes;
            var result = await _departmentService.AddContactPersonAsync(userId, id, contactPersonId, positionId, canEdit, notes, receivesLocationInfo, cancellationToken);

            if (!result)
            {
                return BadRequest("Kontaktperson ist bereits zugewiesen oder wurde nicht gefunden.");
            }

            return NoContent();
        }
        catch (ContactAssignmentConflictException ex)
        {
            return Conflict(ApiResponse<object>.CreateError(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding contact person {ContactPersonId} to department {DepartmentId}", contactPersonId, id);
            return StatusCode(500);
        }
    }

    /// <summary>
    /// Updates the position of a contact person in a department.
    /// </summary>
    [HttpPut("{id}/contact-persons/{contactPersonId}/position")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateContactPosition(
        Guid id,
        Guid contactPersonId,
        [FromQuery] Guid? positionId = null,
        [FromQuery] bool canEdit = false,
        [FromQuery] bool receivesLocationInfo = false,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] AssignContactPersonRequest? body = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = GetUserId();
            var notes = body?.Notes;
            var result = await _departmentService.UpdateContactPersonAsync(userId, id, contactPersonId, positionId, canEdit, notes, receivesLocationInfo, cancellationToken);

            if (!result)
            {
                return BadRequest("Kontaktperson wurde nicht gefunden.");
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating contact position {ContactPersonId} in department {DepartmentId}", contactPersonId, id);
            return StatusCode(500);
        }
    }

    /// <summary>
    /// Removes a contact person from a department.
    /// </summary>
    [HttpDelete("{id}/contact-persons/{contactPersonId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RemoveContactPerson(
        Guid id,
        Guid contactPersonId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = GetUserId();
            var result = await _departmentService.RemoveContactPersonAsync(userId, id, contactPersonId, cancellationToken);

            if (!result)
            {
                return BadRequest("Kontaktperson ist nicht zugewiesen oder wurde nicht gefunden.");
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing contact person {ContactPersonId} from department {DepartmentId}", contactPersonId, id);
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

    private async Task<DepartmentDto> MapToDtoAsync(Department department, Guid userId, CancellationToken cancellationToken)
    {
        // Load club name
        var club = await _clubService.GetByIdAsync(userId, department.ClubId, cancellationToken);
        var clubName = club?.Name;

        // DtoMappingService uses IHttpContextAccessor to get current user, so we don't pass userId
        var dto = await _dtoMappingService.MapDepartmentToDtoAsync(
            department,
            clubName,
            cancellationToken);

        return dto;
    }

    /// <summary>
    /// Top-level departments by name, each followed by its groups (if requested).
    /// </summary>
    private static List<Department> OrderHierarchically(IEnumerable<Department> departments, bool includeGroups)
    {
        var all = departments.ToList();
        var comparer = StringComparer.CurrentCultureIgnoreCase;
        var result = new List<Department>();
        foreach (var department in all.Where(d => !d.IsGroup).OrderBy(d => d.Name, comparer))
        {
            result.Add(department);
            if (includeGroups)
            {
                result.AddRange(all.Where(g => g.ParentDepartmentId == department.Id).OrderBy(g => g.Name, comparer));
            }
        }

        return result;
    }

    private static Department MapFromCreateDto(CreateDepartmentDto dto) => new()
    {
        ClubId = dto.ClubId,
        ParentDepartmentId = dto.ParentDepartmentId,
        Name = dto.Name,
        ShortName = dto.ShortName,
        Description = dto.Description,
        Street = dto.Street,
        PostalCode = dto.PostalCode,
        City = dto.City,
        Email = dto.Email,
        WebsiteUrl = dto.Website,
        SocialMediaLinks = SocialMediaLinkDto.ToEntities(dto.SocialMediaLinks)
    };

    private static Department MapFromUpdateDto(UpdateDepartmentDto dto) => new()
    {
        Name = dto.Name,
        ShortName = dto.ShortName,
        Description = dto.Description,
        Street = dto.Street,
        PostalCode = dto.PostalCode,
        City = dto.City,
        Email = dto.Email,
        WebsiteUrl = dto.Website,
        SocialMediaLinks = SocialMediaLinkDto.ToEntities(dto.SocialMediaLinks)
    };
}
