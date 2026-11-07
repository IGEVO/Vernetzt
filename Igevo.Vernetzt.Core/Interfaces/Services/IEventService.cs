using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.DTOs.Common;

namespace Igevo.Vernetzt.Core.Interfaces.Services;

/// <summary>
/// Service for calendar event management.
/// </summary>
public interface IEventService
{
    Task<Event?> GetByIdAsync(Guid userId, Guid eventId, CancellationToken cancellationToken = default);

    Task<IEnumerable<Event>> GetByClubAsync(
        Guid userId,
        Guid clubId,
        bool includeDepartments,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<Event>> GetByDepartmentAsync(
        Guid userId,
        Guid departmentId,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default);

    Task<PagedResult<Event>> GetByClubPagedAsync(
        Guid userId,
        Guid clubId,
        bool includeDepartments,
        int page,
        int pageSize,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default);

    Task<PagedResult<Event>> GetByDepartmentPagedAsync(
        Guid userId,
        Guid departmentId,
        int page,
        int pageSize,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<Event>> GetPublicGlobalAsync(
        bool highlightsOnly,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default);

    Task<Event> CreateAsync(Guid userId, Event eventEntity, CancellationToken cancellationToken = default);

    Task<Event?> UpdateAsync(Guid userId, Guid eventId, Event eventEntity, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid userId, Guid eventId, CancellationToken cancellationToken = default);

    Task<EventMedia> UploadMediaAsync(
        Guid userId,
        Guid eventId,
        Stream stream,
        string fileName,
        EventMediaType type,
        double? focusX = null,
        double? focusY = null,
        EventMediaUsage usages = EventMediaUsage.None,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes type and/or focal point of a media item. Setting a media item as cover demotes the previous cover.
    /// Returns null if event or media were not found.
    /// </summary>
    Task<EventMedia?> UpdateMediaAsync(
        Guid userId,
        Guid eventId,
        Guid mediaId,
        EventMediaType? type,
        double? focusX,
        double? focusY,
        EventMediaUsage? usages = null,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteMediaAsync(Guid userId, Guid eventId, Guid mediaId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the media item of an event. All events are public, so no authentication is needed.
    /// </summary>
    Task<EventMedia?> GetMediaAsync(Guid eventId, Guid mediaId, CancellationToken cancellationToken = default);
}
