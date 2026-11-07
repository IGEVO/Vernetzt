using System.Security.Claims;
using Igevo.Vernetzt.Core.DTOs;
using Igevo.Vernetzt.Core.DTOs.Clubs;
using Igevo.Vernetzt.Core.DTOs.Common;
using Igevo.Vernetzt.Core.DTOs.ContactPositions;
using Igevo.Vernetzt.Core.DTOs.Departments;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Igevo.Vernetzt.Server.Controllers;

/// <summary>
/// Controller for managing clubs.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
public class ClubsController : ControllerBase
{
    private readonly IClubService _clubService;
    private readonly IDepartmentService _departmentService;
    private readonly Core.Interfaces.Services.IAuthorizationService _authorizationService;
    private readonly IClubTypeRepository _clubTypeRepository;
    private readonly DtoMappingService _dtoMappingService;
    private readonly ILogger<ClubsController> _logger;

    public ClubsController(
        IClubService clubService,
        IDepartmentService departmentService,
        Core.Interfaces.Services.IAuthorizationService authorizationService,
        IClubTypeRepository clubTypeRepository,
        DtoMappingService dtoMappingService,
        ILogger<ClubsController> logger)
    {
        _clubService = clubService;
        _departmentService = departmentService;
        _authorizationService = authorizationService;
        _clubTypeRepository = clubTypeRepository;
        _dtoMappingService = dtoMappingService;
        _logger = logger;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    /// <summary>
    /// Get all clubs accessible to the current user.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<ClubDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        try
        {
            var userId = GetUserId();
            var clubs = await _clubService.GetAccessibleClubsAsync(userId, cancellationToken);
            var clubDtos = new List<ClubDto>();

            foreach (var club in clubs)
            {
                clubDtos.Add(await MapToDtoAsync(club, userId, cancellationToken));
            }

            return Ok(ApiResponse<IEnumerable<ClubDto>>.CreateSuccess(clubDtos));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving clubs");
            return BadRequest(ApiResponse<object>.CreateError("Fehler beim Abrufen der Vereine."));
        }
    }

    /// <summary>
    /// Get a club by ID.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<ClubDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var userId = GetUserId();
            var club = await _clubService.GetByIdAsync(userId, id, cancellationToken);
            if (club == null)
            {
                return NotFound(ApiResponse<object>.CreateError("Verein nicht gefunden oder kein Zugriff."));
            }

            var clubDto = await MapToDtoAsync(club, userId, cancellationToken);
            return Ok(ApiResponse<ClubDto>.CreateSuccess(clubDto));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving club {ClubId}", id);
            return BadRequest(ApiResponse<object>.CreateError("Fehler beim Abrufen des Vereins."));
        }
    }

    /// <summary>
    /// Create a new club (SystemAdmin only).
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<ClubDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create([FromBody] CreateClubDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var userId = GetUserId();
            var club = MapFromCreateDto(dto);
            var createdClub = await _clubService.CreateAsync(userId, club, cancellationToken);
            var clubDto = await MapToDtoAsync(createdClub, userId, cancellationToken);

            return CreatedAtAction(
                nameof(GetById),
                new { id = clubDto.Id },
                ApiResponse<ClubDto>.CreateSuccess(clubDto));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized club creation attempt by user {UserId}", GetUserId());
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating club");
            return BadRequest(ApiResponse<object>.CreateError("Fehler beim Erstellen des Vereins."));
        }
    }

    /// <summary>
    /// Update a club.
    /// </summary>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(ApiResponse<ClubDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateClubDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var userId = GetUserId();
            var club = MapFromUpdateDto(dto);
            var updatedClub = await _clubService.UpdateAsync(userId, id, club, cancellationToken);

            if (updatedClub == null)
            {
                return NotFound(ApiResponse<object>.CreateError("Verein nicht gefunden oder kein Zugriff."));
            }

            var clubDto = await MapToDtoAsync(updatedClub, userId, cancellationToken);
            return Ok(ApiResponse<ClubDto>.CreateSuccess(clubDto));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating club {ClubId}", id);
            return BadRequest(ApiResponse<object>.CreateError("Fehler beim Aktualisieren des Vereins."));
        }
    }

    /// <summary>
    /// Delete a club (SystemAdmin/Manager only).
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await _clubService.DeleteAsync(GetUserId(), id, cancellationToken);
            if (!deleted)
            {
                return NotFound(ApiResponse<object>.CreateError("Verein nicht gefunden oder keine Berechtigung."));
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting club {ClubId}", id);
            return BadRequest(ApiResponse<object>.CreateError("Fehler beim Löschen des Vereins."));
        }
    }

    /// <summary>
    /// Add a contact person to a club with optional position.
    /// </summary>
    [HttpPost("{id}/contact-persons/{contactPersonId}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddContactPerson(
        Guid id,
        Guid contactPersonId,
        [FromQuery] Guid? positionId = null,
        [FromQuery] bool canEdit = false,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] AssignContactPersonRequest? body = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var notes = body?.Notes;
            var success = await _clubService.AddContactPersonAsync(GetUserId(), id, contactPersonId, positionId, canEdit, notes, cancellationToken);
            if (!success)
            {
                return NotFound(ApiResponse<object>.CreateError("Verein oder Kontaktperson nicht gefunden."));
            }

            return Ok(ApiResponse<object>.CreateSuccess(new { message = "Kontaktperson hinzugefügt." }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding contact person to club");
            return BadRequest(ApiResponse<object>.CreateError("Fehler beim Hinzufügen der Kontaktperson."));
        }
    }

    /// <summary>
    /// Update the position of a contact person.
    /// </summary>
    [HttpPut("{id}/contact-persons/{contactPersonId}/position")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateContactPosition(
        Guid id,
        Guid contactPersonId,
        [FromQuery] Guid? positionId = null,
        [FromQuery] bool canEdit = false,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] AssignContactPersonRequest? body = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var notes = body?.Notes;
            var success = await _clubService.UpdateContactPersonAsync(GetUserId(), id, contactPersonId, positionId, canEdit, notes, cancellationToken);
            if (!success)
            {
                return NotFound(ApiResponse<object>.CreateError("Verein oder Kontaktperson nicht gefunden."));
            }

            return Ok(ApiResponse<object>.CreateSuccess(new { message = "Position aktualisiert." }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating contact position");
            return BadRequest(ApiResponse<object>.CreateError("Fehler beim Aktualisieren der Position."));
        }
    }

    /// <summary>
    /// Remove a contact person from a club.
    /// </summary>
    [HttpDelete("{id}/contact-persons/{contactPersonId}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveContactPerson(Guid id, Guid contactPersonId, CancellationToken cancellationToken)
    {
        try
        {
            await _clubService.RemoveContactPersonAsync(GetUserId(), id, contactPersonId, cancellationToken);
            return Ok(ApiResponse<object>.CreateSuccess(new { message = "Kontaktperson entfernt." }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing contact person from club");
            return BadRequest(ApiResponse<object>.CreateError("Fehler beim Entfernen der Kontaktperson."));
        }
    }

    /// <summary>
    /// Search and filter clubs with pagination.
    /// </summary>
    [HttpGet("search")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ClubSummaryDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search([FromQuery] ClubSearchParameters parameters, CancellationToken cancellationToken)
    {
        try
        {
            parameters.Normalize();
            var userId = GetUserId();
            var clubs = await _clubService.GetAccessibleClubsAsync(userId, cancellationToken);

            // Apply filters
            var filtered = clubs.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(parameters.SearchTerm))
            {
                var term = parameters.SearchTerm.ToLower();
                filtered = filtered.Where(c =>
                    c.Name.ToLower().Contains(term) ||
                    (c.City?.ToLower().Contains(term) ?? false) ||
                    (c.Departments != null && c.Departments.Any(d => d.Name.ToLower().Contains(term))));
            }

            if (parameters.ClubTypeId.HasValue)
            {
                filtered = filtered.Where(c => c.ClubTypeId == parameters.ClubTypeId);
            }

            if (!string.IsNullOrWhiteSpace(parameters.City))
            {
                filtered = filtered.Where(c =>
                    c.City?.Equals(parameters.City, StringComparison.OrdinalIgnoreCase) ?? false);
            }

            // Apply sorting
            filtered = parameters.SortBy?.ToLower() switch
            {
                "name" => parameters.SortDirection == "desc"
                    ? filtered.OrderByDescending(c => c.Name)
                    : filtered.OrderBy(c => c.Name),
                "city" => parameters.SortDirection == "desc"
                    ? filtered.OrderByDescending(c => c.City)
                    : filtered.OrderBy(c => c.City),
                "createdat" => parameters.SortDirection == "desc"
                    ? filtered.OrderByDescending(c => c.CreatedAt)
                    : filtered.OrderBy(c => c.CreatedAt),
                _ => filtered.OrderBy(c => c.Name)
            };

            var totalCount = filtered.Count();
            var pagedClubs = filtered.Skip(parameters.Skip).Take(parameters.PageSize).ToList();

            // Map to summary DTOs
            var searchTerm = parameters.SearchTerm?.Trim().ToLower();
            var summaries = new List<ClubSummaryDto>();
            foreach (var club in pagedClubs)
            {
                var clubType = club.ClubTypeId.HasValue
                    ? await _clubTypeRepository.GetByIdAsync(club.ClubTypeId.Value, cancellationToken)
                    : null;

                summaries.Add(new ClubSummaryDto
                {
                    Id = club.Id,
                    Name = club.Name,
                    ClubTypeId = club.ClubTypeId,
                    ClubTypeName = clubType?.Name,
                    City = club.City,
                    Email = club.Email,
                    WebsiteUrl = club.WebsiteUrl,
                    HasAvatar = club.HasAvatar,
                    // Groups are counted with their department, not as separate departments.
                    DepartmentCount = club.Departments?.Count(d => !d.IsGroup) ?? 0,
                    MatchingDepartments = string.IsNullOrEmpty(searchTerm) || club.Departments == null
                        ? new List<DepartmentReferenceDto>()
                        : club.Departments
                            .Where(d => d.Name.ToLower().Contains(searchTerm))
                            .OrderBy(d => d.Name)
                            .Select(d => new DepartmentReferenceDto
                            {
                                Id = d.Id,
                                Name = d.Name,
                                ParentDepartmentName = d.ParentDepartmentId.HasValue
                                    ? club.Departments.FirstOrDefault(p => p.Id == d.ParentDepartmentId)?.Name
                                    : null
                            })
                            .ToList(),
                });
            }

            var result = PagedResult<ClubSummaryDto>.Create(summaries, totalCount, parameters.Page, parameters.PageSize);
            return Ok(ApiResponse<PagedResult<ClubSummaryDto>>.CreateSuccess(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching clubs");
            return BadRequest(ApiResponse<object>.CreateError("Fehler bei der Suche."));
        }
    }

    /// <summary>
    /// Get clubs grouped by club type.
    /// </summary>
    [HttpGet("by-type")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<ClubTypeStats>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByType(CancellationToken cancellationToken)
    {
        try
        {
            var userId = GetUserId();
            var clubs = await _clubService.GetAccessibleClubsAsync(userId, cancellationToken);
            var clubTypes = await _clubTypeRepository.GetAllAsync(cancellationToken);

            var grouped = clubs
                .GroupBy(c => c.ClubTypeId)
                .Select(g => new ClubTypeStats
                {
                    ClubTypeId = g.Key,
                    ClubTypeName = g.Key.HasValue
                        ? clubTypes.FirstOrDefault(ct => ct.Id == g.Key)?.Name ?? "Unbekannt"
                        : "Ohne Typ",
                    ClubCount = g.Count()
                })
                .OrderBy(s => s.ClubTypeName)
                .ToList();

            return Ok(ApiResponse<IEnumerable<ClubTypeStats>>.CreateSuccess(grouped));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting clubs by type");
            return BadRequest(ApiResponse<object>.CreateError("Fehler beim Abrufen der Vereinstypen."));
        }
    }

    /// <summary>
    /// Get list of unique cities from all clubs.
    /// </summary>
    [HttpGet("cities")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<string>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCities(CancellationToken cancellationToken)
    {
        try
        {
            var userId = GetUserId();
            var clubs = await _clubService.GetAccessibleClubsAsync(userId, cancellationToken);

            var cities = clubs
                .Where(c => !string.IsNullOrWhiteSpace(c.City))
                .Select(c => c.City!)
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            return Ok(ApiResponse<IEnumerable<string>>.CreateSuccess(cities));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cities");
            return BadRequest(ApiResponse<object>.CreateError("Fehler beim Abrufen der Städte."));
        }
    }

    private async Task<ClubDto> MapToDtoAsync(Club club, Guid userId, CancellationToken cancellationToken)
    {
        // Use DtoMappingService which handles all visibility checks correctly
        var clubDto = await _dtoMappingService.MapClubToDtoAsync(club, cancellationToken);

        // Load ClubType name if present (include deleted types for display)
        if (club.ClubTypeId.HasValue)
        {
            var clubType = await _clubTypeRepository.GetByIdAsync(club.ClubTypeId.Value, cancellationToken);
            clubDto.ClubTypeName = clubType?.Name;
        }

        // Load departments with visibility filtering; groups are listed on their department page
        var departments = await _departmentService.GetByClubIdAsync(userId, club.Id, cancellationToken);
        var departmentSummaries = new List<DepartmentSummaryDto>();
        foreach (var dept in departments.Where(d => !d.IsGroup).OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var deptSummary = await _dtoMappingService.MapDepartmentToSummaryDtoAsync(dept, cancellationToken);
            departmentSummaries.Add(deptSummary);
        }
        clubDto.Departments = departmentSummaries;

        return clubDto;
    }

    private static Club MapFromCreateDto(CreateClubDto dto) => new()
    {
        Name = dto.Name,
        ShortName = dto.ShortName,
        Description = dto.Description,
        ClubTypeId = dto.ClubTypeId,
        Street = dto.Street,
        PostalCode = dto.PostalCode,
        City = dto.City,
        WebsiteUrl = dto.WebsiteUrl,
        SocialMediaLinks = SocialMediaLinkDto.ToEntities(dto.SocialMediaLinks),
        Email = dto.Email,
        ContactPersonsVisibility = dto.ContactPersonsVisibility
    };

    private static Club MapFromUpdateDto(UpdateClubDto dto) => new()
    {
        Name = dto.Name,
        ShortName = dto.ShortName,
        Description = dto.Description,
        ClubTypeId = dto.ClubTypeId,
        Street = dto.Street,
        PostalCode = dto.PostalCode,
        City = dto.City,
        WebsiteUrl = dto.WebsiteUrl,
        SocialMediaLinks = SocialMediaLinkDto.ToEntities(dto.SocialMediaLinks),
        Email = dto.Email,
        ContactPersonsVisibility = dto.ContactPersonsVisibility
    };
}
