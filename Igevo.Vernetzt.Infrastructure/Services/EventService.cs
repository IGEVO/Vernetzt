using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.DTOs.Common;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Igevo.Vernetzt.Infrastructure.Services;

/// <summary>
/// Service for managing calendar events with authorization checks.
/// </summary>
public class EventService : IEventService
{
    private const int MaxMediaPerEvent = 10;
    private const long MaxMediaFileSizeBytes = 15 * 1024 * 1024; // 15 MB

    private readonly IEventRepository _eventRepository;
    private readonly IAuthorizationService _authorizationService;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IClubRepository _clubRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IFileStorageService _fileStorage;
    private readonly IImageProcessingService _imageProcessing;
    private readonly ILogger<EventService> _logger;

    public EventService(
        IEventRepository eventRepository,
        IAuthorizationService authorizationService,
        IAuditLogRepository auditLogRepository,
        IClubRepository clubRepository,
        IDepartmentRepository departmentRepository,
        IFileStorageService fileStorage,
        IImageProcessingService imageProcessing,
        ILogger<EventService> logger)
    {
        _eventRepository = eventRepository;
        _authorizationService = authorizationService;
        _auditLogRepository = auditLogRepository;
        _clubRepository = clubRepository;
        _departmentRepository = departmentRepository;
        _fileStorage = fileStorage;
        _imageProcessing = imageProcessing;
        _logger = logger;
    }

    public async Task<Event?> GetByIdAsync(Guid userId, Guid eventId, CancellationToken cancellationToken = default)
    {
        // All events are public, so every caller may read them.
        return await _eventRepository.GetByIdAsync(eventId, cancellationToken);
    }

    public async Task<IEnumerable<Event>> GetByClubAsync(
        Guid userId,
        Guid clubId,
        bool includeDepartments,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (!await _authorizationService.CanAccessClubAsync(userId, clubId, cancellationToken))
        {
            _logger.LogWarning("User {UserId} denied access to club events for club {ClubId}", userId, clubId);
            return Enumerable.Empty<Event>();
        }

        var events = await _eventRepository.GetByClubAsync(clubId, includeDepartments, fromUtc, toUtc, cancellationToken);
        return events.OrderBy(e => e.StartUtc).ToList();
    }

    public async Task<IEnumerable<Event>> GetByDepartmentAsync(
        Guid userId,
        Guid departmentId,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (!await _authorizationService.CanAccessDepartmentAsync(userId, departmentId, cancellationToken))
        {
            _logger.LogWarning("User {UserId} denied access to department events for department {DepartmentId}", userId, departmentId);
            return Enumerable.Empty<Event>();
        }

        var events = await _eventRepository.GetByDepartmentAsync(departmentId, fromUtc, toUtc, cancellationToken);
        return events.OrderBy(e => e.StartUtc).ToList();
    }

    public async Task<PagedResult<Event>> GetByClubPagedAsync(
        Guid userId,
        Guid clubId,
        bool includeDepartments,
        int page,
        int pageSize,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (!await _authorizationService.CanAccessClubAsync(userId, clubId, cancellationToken))
        {
            _logger.LogWarning("User {UserId} denied access to paged club events for club {ClubId}", userId, clubId);
            return PagedResult<Event>.Create(Array.Empty<Event>(), 0, Math.Max(1, page), Math.Clamp(pageSize, 1, 100));
        }

        return await _eventRepository.GetByClubPagedAsync(clubId, includeDepartments, page, pageSize, fromUtc, toUtc, cancellationToken);
    }

    public async Task<PagedResult<Event>> GetByDepartmentPagedAsync(
        Guid userId,
        Guid departmentId,
        int page,
        int pageSize,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (!await _authorizationService.CanAccessDepartmentAsync(userId, departmentId, cancellationToken))
        {
            _logger.LogWarning("User {UserId} denied access to paged department events for department {DepartmentId}", userId, departmentId);
            return PagedResult<Event>.Create(Array.Empty<Event>(), 0, Math.Max(1, page), Math.Clamp(pageSize, 1, 100));
        }

        return await _eventRepository.GetByDepartmentPagedAsync(departmentId, page, pageSize, fromUtc, toUtc, cancellationToken);
    }

    public async Task<IEnumerable<Event>> GetPublicGlobalAsync(
        bool highlightsOnly,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var events = await _eventRepository.GetPublicGlobalAsync(highlightsOnly, fromUtc, toUtc, cancellationToken);
        return events.OrderBy(e => e.StartUtc).ToList();
    }

    public async Task<Event> CreateAsync(Guid userId, Event eventEntity, CancellationToken cancellationToken = default)
    {
        await ValidateEntityAssignmentAsync(eventEntity, cancellationToken);
        ValidateDateRange(eventEntity.StartUtc, eventEntity.EndUtc);
        NormalizeEvent(eventEntity);

        if (!await CanEditEventAsync(userId, eventEntity, cancellationToken))
        {
            _logger.LogWarning("User {UserId} denied creation of event", userId);
            throw new UnauthorizedAccessException("You do not have permission to create events for this entity.");
        }

        var created = await _eventRepository.AddAsync(eventEntity, cancellationToken);

        await _auditLogRepository.LogAsync(
            nameof(Event),
            created.Id,
            AuditAction.Create,
            userId,
            null,
            $"Created event: {created.Title}",
            cancellationToken);

        return created;
    }

    public async Task<Event?> UpdateAsync(Guid userId, Guid eventId, Event eventEntity, CancellationToken cancellationToken = default)
    {
        var existing = await _eventRepository.GetByIdAsync(eventId, cancellationToken);
        if (existing == null)
        {
            return null;
        }

        if (!await CanEditEventAsync(userId, existing, cancellationToken))
        {
            _logger.LogWarning("User {UserId} denied update for event {EventId}", userId, eventId);
            return null;
        }

        ValidateDateRange(eventEntity.StartUtc, eventEntity.EndUtc);

        existing.Title = eventEntity.Title?.Trim() ?? string.Empty;
        existing.Description = string.IsNullOrWhiteSpace(eventEntity.Description) ? null : eventEntity.Description.Trim();
        existing.Location = string.IsNullOrWhiteSpace(eventEntity.Location) ? null : eventEntity.Location.Trim();
        existing.StartUtc = eventEntity.StartUtc;
        existing.EndUtc = eventEntity.EndUtc;
        existing.IsAllDay = eventEntity.IsAllDay;
        existing.IsHighlight = eventEntity.IsHighlight;

        var updated = await _eventRepository.UpdateAsync(existing, cancellationToken);

        await _auditLogRepository.LogAsync(
            nameof(Event),
            updated.Id,
            AuditAction.Update,
            userId,
            null,
            $"Updated event: {updated.Title}",
            cancellationToken);

        return updated;
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid eventId, CancellationToken cancellationToken = default)
    {
        var existing = await _eventRepository.GetByIdAsync(eventId, cancellationToken);
        if (existing == null)
        {
            return false;
        }

        if (!await CanEditEventAsync(userId, existing, cancellationToken))
        {
            _logger.LogWarning("User {UserId} denied delete for event {EventId}", userId, eventId);
            return false;
        }

        var deleted = await _eventRepository.DeleteAsync(eventId, cancellationToken);
        if (!deleted)
        {
            return false;
        }

        if (existing.Media.Count > 0)
        {
            await _fileStorage.DeleteDirectoryAsync($"event-media/{eventId}", cancellationToken);
        }

        await _auditLogRepository.LogAsync(
            nameof(Event),
            eventId,
            AuditAction.Delete,
            userId,
            $"Deleted event: {existing.Title}",
            null,
            cancellationToken);

        return true;
    }

    private async Task ValidateEntityAssignmentAsync(Event eventEntity, CancellationToken cancellationToken)
    {
        var hasClub = eventEntity.ClubId.HasValue;
        var hasDepartment = eventEntity.DepartmentId.HasValue;

        if (hasClub == hasDepartment)
        {
            throw new InvalidOperationException("An event must be assigned to exactly one entity (club or department).");
        }

        if (eventEntity.ClubId.HasValue)
        {
            var clubExists = await _clubRepository.ExistsAsync(eventEntity.ClubId.Value, cancellationToken);
            if (!clubExists)
            {
                throw new InvalidOperationException("The selected club does not exist.");
            }
        }

        if (eventEntity.DepartmentId.HasValue)
        {
            var departmentExists = await _departmentRepository.ExistsAsync(eventEntity.DepartmentId.Value, cancellationToken);
            if (!departmentExists)
            {
                throw new InvalidOperationException("The selected department does not exist.");
            }
        }
    }

    private static void ValidateDateRange(DateTime startUtc, DateTime endUtc)
    {
        if (endUtc <= startUtc)
        {
            throw new InvalidOperationException("Event end must be after start.");
        }
    }

    private static void NormalizeEvent(Event eventEntity)
    {
        eventEntity.Title = eventEntity.Title?.Trim() ?? string.Empty;
        eventEntity.Description = string.IsNullOrWhiteSpace(eventEntity.Description) ? null : eventEntity.Description.Trim();
        eventEntity.Location = string.IsNullOrWhiteSpace(eventEntity.Location) ? null : eventEntity.Location.Trim();
    }

    private async Task<bool> CanEditEventAsync(Guid userId, Event eventEntity, CancellationToken cancellationToken)
    {
        if (eventEntity.ClubId.HasValue)
        {
            return await _authorizationService.CanEditClubAsync(userId, eventEntity.ClubId.Value, cancellationToken);
        }

        if (eventEntity.DepartmentId.HasValue)
        {
            return await _authorizationService.CanEditDepartmentAsync(userId, eventEntity.DepartmentId.Value, cancellationToken);
        }

        return false;
    }

    public async Task<EventMedia> UploadMediaAsync(
        Guid userId,
        Guid eventId,
        Stream stream,
        string fileName,
        EventMediaType type,
        double? focusX = null,
        double? focusY = null,
        EventMediaUsage usages = EventMediaUsage.None,
        CancellationToken cancellationToken = default)
    {
        var eventEntity = await _eventRepository.GetByIdAsync(eventId, cancellationToken)
            ?? throw new InvalidOperationException("Termin nicht gefunden.");

        if (!await CanEditEventAsync(userId, eventEntity, cancellationToken))
        {
            _logger.LogWarning("User {UserId} denied media upload for event {EventId}", userId, eventId);
            throw new UnauthorizedAccessException("You do not have permission to upload media for this event.");
        }

        if (!Enum.IsDefined(type))
        {
            throw new InvalidOperationException("Ungültiger Medientyp.");
        }

        var existingMedia = await _eventRepository.GetMediaByEventAsync(eventId, cancellationToken);
        if (existingMedia.Count >= MaxMediaPerEvent)
        {
            throw new InvalidOperationException($"Ein Termin kann höchstens {MaxMediaPerEvent} Dateien haben.");
        }

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0)
        {
            throw new InvalidOperationException("Die Datei ist leer.");
        }

        if (buffer.Length > MaxMediaFileSizeBytes)
        {
            throw new InvalidOperationException("Die Datei ist zu groß (maximal 15 MB).");
        }

        buffer.Position = 0;
        var processed = await _imageProcessing.ProcessEventMediaAsync(buffer, fileName, cancellationToken);
        if (!processed.Success)
        {
            throw new InvalidOperationException(processed.ErrorMessage ?? "Die Datei konnte nicht verarbeitet werden.");
        }

        usages &= AllUsages;
        if ((type is EventMediaType.Cover or EventMediaType.Poster || usages != EventMediaUsage.None) && processed.Variants.Count == 0)
        {
            throw new InvalidOperationException("Für diese Datei konnte keine Vorschau erzeugt werden. Als Titelbild oder Plakat bitte ein Bild (JPG, PNG, WebP, HEIC) verwenden.");
        }

        var mediaId = Guid.NewGuid();
        var displayName = Path.GetFileNameWithoutExtension(Path.GetFileName(fileName));
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = "bild";
        }

        var storedName = displayName + processed.Extension;
        var storedPath = await _fileStorage.UploadFileAsync(
            new MemoryStream(processed.Data, writable: false),
            storedName,
            processed.ContentType,
            $"event-media/{eventId}",
            cancellationToken);

        var variantWidths = new List<int>();
        try
        {
            foreach (var variant in processed.Variants)
            {
                await _fileStorage.UploadFileWithExactPathAsync(
                    new MemoryStream(variant.Data, writable: false),
                    EventMedia.GetVariantPath(eventId, mediaId, variant.Width),
                    cancellationToken);
                variantWidths.Add(variant.Width);
            }
        }
        catch
        {
            await _fileStorage.DeleteFileAsync(storedPath, cancellationToken);
            await _fileStorage.DeleteDirectoryAsync(EventMedia.GetVariantDirectory(eventId, mediaId), cancellationToken);
            throw;
        }

        // Only one cover and one poster per event: a new one demotes the previous one to a regular image.
        var demoted = DemoteOtherOfType(existingMedia, null, type);

        // Each view is assigned to at most one media item.
        foreach (var other in existingMedia.Where(m => (m.Usages & usages) != EventMediaUsage.None))
        {
            other.Usages &= ~usages;
            if (!demoted.Contains(other))
            {
                demoted.Add(other);
            }
        }

        var media = new EventMedia
        {
            Id = mediaId,
            EventId = eventId,
            Type = type,
            FilePath = storedPath,
            FileName = storedName,
            ContentType = processed.ContentType,
            FileSize = processed.Data.LongLength,
            SortOrder = existingMedia.Count == 0 ? 0 : existingMedia.Max(m => m.SortOrder) + 1,
            FocusX = ClampFocus(focusX),
            FocusY = ClampFocus(focusY),
            Usages = usages,
            Width = processed.Width,
            Height = processed.Height,
            VariantWidths = variantWidths.Count > 0 ? string.Join(',', variantWidths) : null,
            CreatedById = userId,
            UpdatedById = userId
        };

        if (demoted.Count > 0)
        {
            await _eventRepository.UpdateMediaAsync(demoted, cancellationToken);
        }

        var created = await _eventRepository.AddMediaAsync(media, cancellationToken);

        await _auditLogRepository.LogAsync(
            nameof(EventMedia),
            created.Id,
            AuditAction.Create,
            userId,
            null,
            $"Uploaded {type} media '{storedName}' for event {eventId}",
            cancellationToken);

        return created;
    }

    public async Task<EventMedia?> UpdateMediaAsync(
        Guid userId,
        Guid eventId,
        Guid mediaId,
        EventMediaType? type,
        double? focusX,
        double? focusY,
        EventMediaUsage? usages = null,
        CancellationToken cancellationToken = default)
    {
        var eventEntity = await _eventRepository.GetByIdAsync(eventId, cancellationToken);
        if (eventEntity == null)
        {
            return null;
        }

        if (!await CanEditEventAsync(userId, eventEntity, cancellationToken))
        {
            _logger.LogWarning("User {UserId} denied media update for event {EventId}", userId, eventId);
            throw new UnauthorizedAccessException("You do not have permission to edit media for this event.");
        }

        var allMedia = await _eventRepository.GetMediaByEventAsync(eventId, cancellationToken);
        var media = allMedia.FirstOrDefault(m => m.Id == mediaId);
        if (media == null)
        {
            return null;
        }

        var changed = new List<EventMedia> { media };

        if (type.HasValue && type.Value != media.Type)
        {
            if (!Enum.IsDefined(type.Value))
            {
                throw new InvalidOperationException("Ungültiger Medientyp.");
            }

            if (type.Value is EventMediaType.Cover or EventMediaType.Poster)
            {
                if (!media.HasPreview)
                {
                    var label = type.Value == EventMediaType.Cover ? "Titelbild" : "Plakat";
                    throw new InvalidOperationException($"Diese Datei hat keine Bildvorschau und kann nicht als {label} verwendet werden.");
                }

                changed.AddRange(DemoteOtherOfType(allMedia, mediaId, type.Value));
            }

            media.Type = type.Value;
        }

        if (usages.HasValue)
        {
            var newUsages = usages.Value & AllUsages;
            if (newUsages != EventMediaUsage.None && !media.HasPreview)
            {
                throw new InvalidOperationException("Diese Datei hat keine Bildvorschau und kann keiner Ansicht zugewiesen werden.");
            }

            foreach (var other in allMedia.Where(m => m.Id != mediaId && (m.Usages & newUsages) != EventMediaUsage.None))
            {
                other.Usages &= ~newUsages;
                if (!changed.Contains(other))
                {
                    changed.Add(other);
                }
            }

            media.Usages = newUsages;
        }

        if (focusX.HasValue)
        {
            media.FocusX = ClampFocus(focusX);
        }

        if (focusY.HasValue)
        {
            media.FocusY = ClampFocus(focusY);
        }

        media.UpdatedById = userId;
        await _eventRepository.UpdateMediaAsync(changed, cancellationToken);

        await _auditLogRepository.LogAsync(
            nameof(EventMedia),
            media.Id,
            AuditAction.Update,
            userId,
            null,
            $"Updated media '{media.FileName}' of event {eventId} (type {media.Type}, usages {media.Usages}, focus {media.FocusX:0.00}/{media.FocusY:0.00})",
            cancellationToken);

        return media;
    }

    public async Task<bool> DeleteMediaAsync(Guid userId, Guid eventId, Guid mediaId, CancellationToken cancellationToken = default)
    {
        var eventEntity = await _eventRepository.GetByIdAsync(eventId, cancellationToken);
        if (eventEntity == null)
        {
            return false;
        }

        if (!await CanEditEventAsync(userId, eventEntity, cancellationToken))
        {
            _logger.LogWarning("User {UserId} denied media deletion for event {EventId}", userId, eventId);
            throw new UnauthorizedAccessException("You do not have permission to delete media for this event.");
        }

        var media = await _eventRepository.GetMediaByIdAsync(mediaId, cancellationToken);
        if (media == null || media.EventId != eventId)
        {
            return false;
        }

        await _fileStorage.DeleteFileAsync(media.FilePath, cancellationToken);
        if (media.HasPreview)
        {
            await _fileStorage.DeleteDirectoryAsync(EventMedia.GetVariantDirectory(eventId, mediaId), cancellationToken);
        }

        var deleted = await _eventRepository.DeleteMediaAsync(mediaId, cancellationToken);

        if (deleted)
        {
            await _auditLogRepository.LogAsync(
                nameof(EventMedia),
                mediaId,
                AuditAction.Delete,
                userId,
                $"Deleted media '{media.FileName}' from event {eventId}",
                null,
                cancellationToken);
        }

        return deleted;
    }

    public async Task<EventMedia?> GetMediaAsync(
        Guid eventId,
        Guid mediaId,
        CancellationToken cancellationToken = default)
    {
        var eventEntity = await _eventRepository.GetByIdAsync(eventId, cancellationToken);
        if (eventEntity == null)
        {
            return null;
        }

        // All events are public: media is served without authentication.
        return eventEntity.Media.FirstOrDefault(m => m.Id == mediaId);
    }

    /// <summary>
    /// Cover and poster exist at most once per event: every other item of that type becomes a regular image.
    /// Returns the items that were changed.
    /// </summary>
    private static List<EventMedia> DemoteOtherOfType(IEnumerable<EventMedia> all, Guid? exceptId, EventMediaType type)
    {
        var demoted = new List<EventMedia>();
        if (type is not (EventMediaType.Cover or EventMediaType.Poster))
        {
            return demoted;
        }

        foreach (var other in all.Where(m => m.Id != exceptId && m.Type == type))
        {
            other.Type = EventMediaType.Image;
            demoted.Add(other);
        }

        return demoted;
    }

    private const EventMediaUsage AllUsages = EventMediaUsage.List | EventMediaUsage.Header | EventMediaUsage.Card;

    private static double ClampFocus(double? value) =>
        value.HasValue && double.IsFinite(value.Value) ? Math.Clamp(value.Value, 0d, 1d) : 0.5d;
}
