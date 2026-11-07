using Igevo.Vernetzt.Core.DTOs.ContactPositions;

namespace Igevo.Vernetzt.Core.Interfaces.Services;

/// <summary>
/// Service interface for ContactPosition management
/// </summary>
public interface IContactPositionService
{
    Task<ContactPositionDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<ContactPositionDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ContactPositionDto> CreateAsync(CreateContactPositionDto dto, CancellationToken cancellationToken = default);
    Task<ContactPositionDto> UpdateAsync(Guid id, UpdateContactPositionDto dto, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
