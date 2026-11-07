using Igevo.Vernetzt.Core.DTOs.Common;

namespace Igevo.Vernetzt.Core.Interfaces.Services;

/// <summary>
/// Service for aggregating dashboard data in a single call.
/// </summary>
public interface IDashboardService
{
    /// <summary>
    /// Gets all data needed for the dashboard: statistics and user's own clubs/departments.
    /// </summary>
    /// <param name="userId">The current user's ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Combined dashboard data.</returns>
    Task<DashboardDataDto> GetDashboardDataAsync(Guid userId, CancellationToken cancellationToken = default);
}
