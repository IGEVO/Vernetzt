using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.DTOs.Common;

namespace Igevo.Vernetzt.Core.Interfaces.Repositories;

/// <summary>
/// Repository interface for calendar events.
/// </summary>
public interface IEventRepository : IRepository<Event>
{
    /// <summary>
    /// Gets events assigned to a club. Optionally includes events of departments and groups in that club.
    /// </summary>
    Task<IEnumerable<Event>> GetByClubAsync(
        Guid clubId,
        bool includeDepartments,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets events assigned to a department, including the events of its groups.
    /// </summary>
    Task<IEnumerable<Event>> GetByDepartmentAsync(
        Guid departmentId,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets public events for the whole application.
    /// </summary>
    Task<IEnumerable<Event>> GetPublicGlobalAsync(
        bool highlightsOnly,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default);

    Task<PagedResult<Event>> GetByClubPagedAsync(
        Guid clubId,
        bool includeDepartments,
        int page,
        int pageSize,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default);

    Task<PagedResult<Event>> GetByDepartmentPagedAsync(
        Guid departmentId,
        int page,
        int pageSize,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default);

    Task<int> CountMediaAsync(Guid eventId, CancellationToken cancellationToken = default);

    Task<EventMedia?> GetMediaByIdAsync(Guid mediaId, CancellationToken cancellationToken = default);

    Task<EventMedia> AddMediaAsync(EventMedia media, CancellationToken cancellationToken = default);

    Task<bool> DeleteMediaAsync(Guid mediaId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EventMedia>> GetMediaByEventAsync(Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists changes to the given media items (type, focal point, sort order).
    /// </summary>
    Task UpdateMediaAsync(IEnumerable<EventMedia> media, CancellationToken cancellationToken = default);
}
