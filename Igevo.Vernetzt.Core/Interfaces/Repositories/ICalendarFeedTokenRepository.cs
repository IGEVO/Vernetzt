using Igevo.Vernetzt.Core.Entities;

namespace Igevo.Vernetzt.Core.Interfaces.Repositories;

/// <summary>
/// Repository for calendar feed tokens.
/// </summary>
public interface ICalendarFeedTokenRepository : IRepository<CalendarFeedToken>
{
    Task<CalendarFeedToken?> GetActiveByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task<IEnumerable<CalendarFeedToken>> GetByClubAsync(Guid clubId, CancellationToken cancellationToken = default);
    Task<IEnumerable<CalendarFeedToken>> GetByDepartmentAsync(Guid departmentId, CancellationToken cancellationToken = default);
    Task<IEnumerable<CalendarFeedToken>> GetGlobalAsync(CancellationToken cancellationToken = default);
}
