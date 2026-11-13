using Igevo.Vernetzt.Core.DTOs.Public;

namespace Igevo.Vernetzt.Core.Interfaces.Services;

public interface IPublicDirectoryService
{
    Task<PublicPagedResult<PublicClubSummaryDto>> GetClubsAsync(
        string? query, string? city, Guid? clubTypeId, int page, int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>A random selection of clubs that changes with every call (e.g. for a start page).</summary>
    Task<PublicPagedResult<PublicClubSummaryDto>> GetRandomClubsAsync(int count, CancellationToken cancellationToken = default);
    Task<PublicClubDto?> GetClubAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PublicPagedResult<PublicDepartmentSummaryDto>> GetDepartmentsAsync(
        int page, int pageSize, CancellationToken cancellationToken = default);
    Task<PublicDepartmentDto?> GetDepartmentAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PublicClubTypeDto>> GetClubTypesAsync(CancellationToken cancellationToken = default);
    Task<PublicPagedResult<PublicEventDto>> GetEventsAsync(
        DateTime? fromUtc, DateTime? toUtc, Guid? clubId, Guid? departmentId,
        bool highlightsOnly, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<PublicEventDto?> GetEventAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PublicContactDetailsDto?> GetClubContactDetailsAsync(Guid clubId, CancellationToken cancellationToken = default);
    Task<PublicContactDetailsDto?> GetDepartmentContactDetailsAsync(
        Guid departmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Public email address and phone number of a contact person (by assignment id), or null when
    /// the assignment does not exist, its contact persons are not public or it has no public field.
    /// </summary>
    Task<PublicContactDetailsDto?> GetContactDetailsAsync(Guid contactId, CancellationToken cancellationToken = default);
}
