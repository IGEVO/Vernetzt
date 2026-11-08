using System.Security.Claims;
using Igevo.Vernetzt.Core.DTOs;
using Igevo.Vernetzt.Core.DTOs.Common;
using Igevo.Vernetzt.Core.DTOs.Events;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Igevo.Vernetzt.Server.Controllers;

/// <summary>
/// Controller for managing calendar events.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
public class EventsController : ControllerBase
{
    private readonly IEventService _eventService;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<EventsController> _logger;

    public EventsController(IEventService eventService, IFileStorageService fileStorage, ILogger<EventsController> logger)
    {
        _eventService = eventService;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<EventDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EventDto>>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = GetUserId();
            var eventEntity = await _eventService.GetByIdAsync(userId, id, cancellationToken);
            if (eventEntity == null)
            {
                return NotFound(ApiResponse<object>.CreateError("Termin nicht gefunden oder kein Zugriff.", "NOT_FOUND"));
            }

            return Ok(ApiResponse<EventDto>.CreateSuccess(MapToDto(eventEntity)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting event {EventId}", id);
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim Abrufen des Termins aufgetreten."));
        }
    }

    [HttpGet("club/{clubId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<EventDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IEnumerable<EventDto>>>> GetByClub(
        Guid clubId,
        [FromQuery] bool includeDepartments = true,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = GetUserId();
            var events = await _eventService.GetByClubAsync(userId, clubId, includeDepartments, fromUtc, toUtc, cancellationToken);
            var result = events.Select(e => MapToDto(e));
            return Ok(ApiResponse<IEnumerable<EventDto>>.CreateSuccess(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting events for club {ClubId}", clubId);
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim Abrufen der Vereinstermine aufgetreten."));
        }
    }

    [HttpGet("club/{clubId:guid}/paged")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<EventDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<EventDto>>>> GetByClubPaged(
        Guid clubId,
        [FromQuery] bool includeDepartments = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = GetUserId();
            var result = await _eventService.GetByClubPagedAsync(userId, clubId, includeDepartments, page, pageSize, fromUtc, toUtc, cancellationToken);
            var dto = PagedResult<EventDto>.Create(result.Items.Select(e => MapToDto(e)), result.TotalCount, result.Page, result.PageSize);
            return Ok(ApiResponse<PagedResult<EventDto>>.CreateSuccess(dto));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting paged events for club {ClubId}", clubId);
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim Abrufen der Vereinstermine aufgetreten."));
        }
    }

    [HttpGet("department/{departmentId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<EventDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IEnumerable<EventDto>>>> GetByDepartment(
        Guid departmentId,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = GetUserId();
            var events = await _eventService.GetByDepartmentAsync(userId, departmentId, fromUtc, toUtc, cancellationToken);
            var result = events.Select(e => MapToDto(e));
            return Ok(ApiResponse<IEnumerable<EventDto>>.CreateSuccess(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting events for department {DepartmentId}", departmentId);
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim Abrufen der Abteilungstermine aufgetreten."));
        }
    }

    [HttpGet("department/{departmentId:guid}/paged")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<EventDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<EventDto>>>> GetByDepartmentPaged(
        Guid departmentId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = GetUserId();
            var result = await _eventService.GetByDepartmentPagedAsync(userId, departmentId, page, pageSize, fromUtc, toUtc, cancellationToken);
            var dto = PagedResult<EventDto>.Create(result.Items.Select(e => MapToDto(e)), result.TotalCount, result.Page, result.PageSize);
            return Ok(ApiResponse<PagedResult<EventDto>>.CreateSuccess(dto));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting paged events for department {DepartmentId}", departmentId);
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim Abrufen der Abteilungstermine aufgetreten."));
        }
    }

    [HttpGet("public/global")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<EventDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IEnumerable<EventDto>>>> GetPublicGlobal(
        [FromQuery] bool highlightsOnly = false,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var events = await _eventService.GetPublicGlobalAsync(highlightsOnly, fromUtc, toUtc, cancellationToken);
            var result = events.Select(e => MapToDto(e));
            return Ok(ApiResponse<IEnumerable<EventDto>>.CreateSuccess(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting global public events");
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim Abrufen der öffentlichen Termine aufgetreten."));
        }
    }

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<EventDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<EventDto>>> Create(
        [FromBody] CreateEventDto dto,
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
            var created = await _eventService.CreateAsync(userId, MapFromCreateDto(dto), cancellationToken);

            return CreatedAtAction(
                nameof(GetById),
                new { id = created.Id },
                ApiResponse<EventDto>.CreateSuccess(MapToDto(created)));
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(403, ApiResponse<object>.CreateError("Du hast keine Berechtigung, diesen Termin anzulegen.", "FORBIDDEN"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.CreateError(ex.Message, "INVALID_OPERATION"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating event");
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim Erstellen des Termins aufgetreten."));
        }
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<EventDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<EventDto>>> Update(
        Guid id,
        [FromBody] UpdateEventDto dto,
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
            var updated = await _eventService.UpdateAsync(userId, id, MapFromUpdateDto(dto), cancellationToken);

            if (updated == null)
            {
                return NotFound(ApiResponse<object>.CreateError("Termin nicht gefunden oder keine Berechtigung.", "NOT_FOUND"));
            }

            return Ok(ApiResponse<EventDto>.CreateSuccess(MapToDto(updated)));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.CreateError(ex.Message, "INVALID_OPERATION"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating event {EventId}", id);
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim Aktualisieren des Termins aufgetreten."));
        }
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = GetUserId();
            var deleted = await _eventService.DeleteAsync(userId, id, cancellationToken);
            if (!deleted)
            {
                return NotFound(ApiResponse<object>.CreateError("Termin nicht gefunden oder keine Berechtigung.", "NOT_FOUND"));
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting event {EventId}", id);
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim Löschen des Termins aufgetreten."));
        }
    }

    [HttpGet("{eventId:guid}/media/{mediaId:guid}/file")]
    [AllowAnonymous]
    public async Task<IActionResult> GetMediaFile(Guid eventId, Guid mediaId, CancellationToken cancellationToken = default)
    {
        try
        {
            var media = await _eventService.GetMediaAsync(eventId, mediaId, cancellationToken);
            if (media == null)
            {
                return NotFound();
            }

            var stream = await _fileStorage.DownloadFileAsync(media.FilePath, cancellationToken);
            SetMediaCacheHeaders();

            // Images are shown inline (lightbox), documents are downloaded with their file name.
            return media.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                ? File(stream, media.ContentType)
                : File(stream, media.ContentType, media.FileName);
        }
        catch (FileNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error serving media {MediaId} for event {EventId}", mediaId, eventId);
            return NotFound();
        }
    }

    /// <summary>
    /// Serves a WebP preview of an image or PDF. Returns the smallest stored variant that is at least
    /// <paramref name="width"/> pixels wide (or the largest available one).
    /// </summary>
    [HttpGet("{eventId:guid}/media/{mediaId:guid}/preview/{width:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetMediaPreview(Guid eventId, Guid mediaId, int width, CancellationToken cancellationToken = default)
    {
        try
        {
            var media = await _eventService.GetMediaAsync(eventId, mediaId, cancellationToken);
            if (media == null)
            {
                return NotFound();
            }

            var widths = media.GetVariantWidths();
            if (widths.Count == 0)
            {
                return NotFound();
            }

            var chosen = widths.FirstOrDefault(w => w >= width, widths[^1]);
            var stream = await _fileStorage.DownloadFileAsync(EventMedia.GetVariantPath(eventId, mediaId, chosen), cancellationToken);
            SetMediaCacheHeaders();
            return File(stream, "image/webp");
        }
        catch (FileNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error serving preview of media {MediaId} for event {EventId}", mediaId, eventId);
            return NotFound();
        }
    }

    /// <summary>
    /// Media files are immutable per id and all events are public, so shared caches may store them.
    /// </summary>
    private void SetMediaCacheHeaders()
    {
        Response.Headers.CacheControl = "public, max-age=604800";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
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

    [HttpPost("{id:guid}/media")]
    [ProducesResponseType(typeof(ApiResponse<EventMediaDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [RequestSizeLimit(16 * 1024 * 1024)]
    public async Task<ActionResult<ApiResponse<EventMediaDto>>> UploadMedia(
        Guid id,
        IFormFile file,
        [FromForm] EventMediaType type = EventMediaType.Attachment,
        [FromForm] double? focusX = null,
        [FromForm] double? focusY = null,
        [FromForm] EventMediaUsage usages = EventMediaUsage.None,
        CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(ApiResponse<object>.CreateError("Keine Datei ausgewählt.", "VALIDATION_ERROR"));
        }

        try
        {
            var userId = GetUserId();
            using var stream = file.OpenReadStream();
            var media = await _eventService.UploadMediaAsync(userId, id, stream, file.FileName, type, focusX, focusY, usages, cancellationToken);
            return StatusCode(201, ApiResponse<EventMediaDto>.CreateSuccess(MapMediaToDto(media)));
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(403, ApiResponse<object>.CreateError("Keine Berechtigung zum Hochladen von Medien.", "FORBIDDEN"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.CreateError(ex.Message, "INVALID_OPERATION"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading media for event {EventId}", id);
            return StatusCode(500, ApiResponse<object>.CreateError("Fehler beim Hochladen der Datei."));
        }
    }

    [HttpPut("{id:guid}/media/{mediaId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<EventMediaDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EventMediaDto>>> UpdateMedia(
        Guid id,
        Guid mediaId,
        [FromBody] UpdateEventMediaDto dto,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = GetUserId();
            var media = await _eventService.UpdateMediaAsync(userId, id, mediaId, dto.Type, dto.FocusX, dto.FocusY, dto.Usages, cancellationToken);
            if (media == null)
            {
                return NotFound(ApiResponse<object>.CreateError("Mediendatei nicht gefunden.", "NOT_FOUND"));
            }

            return Ok(ApiResponse<EventMediaDto>.CreateSuccess(MapMediaToDto(media)));
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(403, ApiResponse<object>.CreateError("Keine Berechtigung zum Bearbeiten von Medien.", "FORBIDDEN"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.CreateError(ex.Message, "INVALID_OPERATION"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating media {MediaId} for event {EventId}", mediaId, id);
            return StatusCode(500, ApiResponse<object>.CreateError("Fehler beim Speichern der Mediendatei."));
        }
    }

    [HttpDelete("{id:guid}/media/{mediaId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteMedia(Guid id, Guid mediaId, CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = GetUserId();
            var deleted = await _eventService.DeleteMediaAsync(userId, id, mediaId, cancellationToken);
            if (!deleted)
            {
                return NotFound(ApiResponse<object>.CreateError("Mediendatei nicht gefunden.", "NOT_FOUND"));
            }

            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(403, ApiResponse<object>.CreateError("Keine Berechtigung zum Löschen von Medien.", "FORBIDDEN"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting media {MediaId} for event {EventId}", mediaId, id);
            return StatusCode(500, ApiResponse<object>.CreateError("Fehler beim Löschen der Datei."));
        }
    }

    private static EventDto MapToDto(Event eventEntity, bool canEdit = false)
    {
        var media = eventEntity.Media
            .OrderBy(m => m.SortOrder)
            .Select(MapMediaToDto)
            .ToList();
        var image = ResolveDisplayImage(media);

        return new EventDto
        {
            Id = eventEntity.Id,
            Title = eventEntity.Title,
            Description = eventEntity.Description,
            Location = eventEntity.Location,
            StartUtc = eventEntity.StartUtc,
            EndUtc = eventEntity.EndUtc,
            IsAllDay = eventEntity.IsAllDay,
            IsHighlight = eventEntity.IsHighlight,
            ClubId = eventEntity.ClubId,
            DepartmentId = eventEntity.DepartmentId,
            // Full organizer path: club > department > group
            ClubName = eventEntity.Club?.Name ?? eventEntity.Department?.Club?.Name,
            DepartmentName = eventEntity.Department?.Name,
            ParentDepartmentName = eventEntity.Department?.ParentDepartment?.Name,
            CanEdit = canEdit,
            CreatedAt = eventEntity.CreatedAt,
            UpdatedAt = eventEntity.UpdatedAt,
            Media = media,
            Image = image,
            ListImage = ResolveUsageImage(media, EventMediaUsage.List) ?? image,
            HeaderImage = ResolveUsageImage(media, EventMediaUsage.Header) ?? image,
            CardImage = ResolveUsageImage(media, EventMediaUsage.Card) ?? image,
            PosterImage = ResolvePoster(media)
        };
    }

    /// <summary>
    /// Display image fallback chain: explicit cover → other image → any media with preview (the poster only as a last resort).
    /// </summary>
    internal static EventMediaDto? ResolveDisplayImage(IReadOnlyList<EventMediaDto> media)
    {
        var withPreview = media.Where(m => m.Variants.Count > 0).ToList();
        return withPreview.FirstOrDefault(m => m.Type == EventMediaType.Cover)
            ?? withPreview.FirstOrDefault(m => m.Type == EventMediaType.Image)
            ?? withPreview.FirstOrDefault();
    }

    /// <summary>The event's poster if it has a preview; there is no fallback to other media.</summary>
    internal static EventMediaDto? ResolvePoster(IReadOnlyList<EventMediaDto> media) =>
        media.FirstOrDefault(m => m.Type == EventMediaType.Poster && m.Variants.Count > 0);

    private static EventMediaDto? ResolveUsageImage(IReadOnlyList<EventMediaDto> media, EventMediaUsage usage) =>
        media.FirstOrDefault(m => m.Variants.Count > 0 && m.Usages.HasFlag(usage));

    private static EventMediaDto MapMediaToDto(EventMedia media)
    {
        var baseUrl = $"/api/events/{media.EventId}/media/{media.Id}";
        return new EventMediaDto
        {
            Id = media.Id,
            Type = media.Type,
            FileName = media.FileName,
            ContentType = media.ContentType,
            FileSize = media.FileSize,
            SortOrder = media.SortOrder,
            FileUrl = $"{baseUrl}/file",
            FocusX = media.FocusX,
            FocusY = media.FocusY,
            Usages = media.Usages,
            Width = media.Width,
            Height = media.Height,
            Variants = media.GetVariantWidths()
                .Select(w => new EventMediaVariantDto { Width = w, Url = $"{baseUrl}/preview/{w}" })
                .ToList()
        };
    }

    private static Event MapFromCreateDto(CreateEventDto dto)
    {
        return new Event
        {
            Title = dto.Title,
            Description = dto.Description,
            Location = dto.Location,
            StartUtc = dto.StartUtc,
            EndUtc = dto.EndUtc,
            IsAllDay = dto.IsAllDay,
            IsHighlight = dto.IsHighlight,
            ClubId = dto.ClubId,
            DepartmentId = dto.DepartmentId
        };
    }

    private static Event MapFromUpdateDto(UpdateEventDto dto)
    {
        return new Event
        {
            Title = dto.Title,
            Description = dto.Description,
            Location = dto.Location,
            StartUtc = dto.StartUtc,
            EndUtc = dto.EndUtc,
            IsAllDay = dto.IsAllDay,
            IsHighlight = dto.IsHighlight
        };
    }
}
