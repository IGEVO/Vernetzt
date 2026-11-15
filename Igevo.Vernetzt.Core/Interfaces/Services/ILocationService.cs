using Igevo.Vernetzt.Core.DTOs.Locations;

namespace Igevo.Vernetzt.Core.Interfaces.Services;

/// <summary>
/// Central list of locations ("Standorte"), maintained by system admins and managers.
/// Validation errors are reported as <see cref="InvalidOperationException"/>,
/// unknown ids as <see cref="KeyNotFoundException"/>.
/// </summary>
public interface ILocationService
{
    /// <summary>All locations ordered by name, including usage counts.</summary>
    Task<IReadOnlyList<LocationDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<LocationDto> CreateAsync(Guid userId, SaveLocationDto dto, CancellationToken cancellationToken = default);
    Task<LocationDto> UpdateAsync(Guid userId, Guid id, SaveLocationDto dto, CancellationToken cancellationToken = default);

    /// <summary>Deletes the location; its links to clubs, departments and groups are removed.</summary>
    Task DeleteAsync(Guid userId, Guid id, CancellationToken cancellationToken = default);
}
