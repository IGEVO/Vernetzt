using Igevo.Vernetzt.Core.DTOs.Events;

namespace Igevo.Vernetzt.Core.Interfaces.Services;

/// <summary>
/// Service for anonymous calendar feeds and token lifecycle.
/// </summary>
public interface ICalendarFeedService
{
    Task<CalendarFeedTokenCreatedDto> CreateTokenAsync(Guid userId, CreateCalendarFeedTokenDto dto, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CalendarFeedTokenDto>> GetTokensAsync(
        Guid userId,
        Guid? clubId = null,
        Guid? departmentId = null,
        bool globalOnly = false,
        CancellationToken cancellationToken = default);

    Task<CalendarFeedTokenCreatedDto?> RotateTokenAsync(Guid userId, Guid tokenId, CancellationToken cancellationToken = default);

    Task<bool> RevokeTokenAsync(Guid userId, Guid tokenId, CancellationToken cancellationToken = default);

    Task<CalendarFeedContentDto?> GetFeedByTokenAsync(string token, CancellationToken cancellationToken = default);
}
