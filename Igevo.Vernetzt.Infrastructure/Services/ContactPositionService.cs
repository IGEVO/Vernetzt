using Igevo.Vernetzt.Core.DTOs.ContactPositions;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Igevo.Vernetzt.Infrastructure.Services;

/// <summary>
/// Service for managing contact positions (like "Vorstand", "Trainer", etc.)
/// </summary>
public class ContactPositionService : IContactPositionService
{
    private readonly IContactPositionRepository _repository;
    private readonly ILogger<ContactPositionService> _logger;

    public ContactPositionService(
        IContactPositionRepository repository,
        ILogger<ContactPositionService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<ContactPositionDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var position = await _repository.GetByIdAsync(id, cancellationToken);
        return position == null ? null : MapToDto(position);
    }

    public async Task<List<ContactPositionDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var positions = await _repository.GetAllAsync(cancellationToken);
        return positions.Select(MapToDto).ToList();
    }

    public async Task<ContactPositionDto> CreateAsync(CreateContactPositionDto dto, CancellationToken cancellationToken = default)
    {
        var position = new ContactPosition
        {
            Name = dto.Name.Trim(),
            Description = dto.Description?.Trim()
        };

        var created = await _repository.CreateAsync(position, cancellationToken);
        _logger.LogInformation("Created contact position {PositionId} ({PositionName})", created.Id, LogSanitizer.Clean(created.Name));

        return MapToDto(created);
    }

    public async Task<ContactPositionDto> UpdateAsync(Guid id, UpdateContactPositionDto dto, CancellationToken cancellationToken = default)
    {
        var existing = await _repository.GetByIdAsync(id, cancellationToken);
        if (existing == null)
        {
            throw new KeyNotFoundException($"Contact position with ID {id} not found");
        }

        existing.Name = dto.Name.Trim();
        existing.Description = dto.Description?.Trim();

        var updated = await _repository.UpdateAsync(existing, cancellationToken);
        _logger.LogInformation("Updated contact position {PositionId} ({PositionName})", updated.Id, updated.Name);

        return MapToDto(updated);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var deleted = await _repository.DeleteAsync(id, cancellationToken);
        if (deleted)
        {
            _logger.LogInformation("Deleted contact position {PositionId}", id);
        }
        return deleted;
    }

    private static ContactPositionDto MapToDto(ContactPosition position)
    {
        return new ContactPositionDto
        {
            Id = position.Id,
            Name = position.Name,
            Description = position.Description,
            CreatedAt = position.CreatedAt,
            UpdatedAt = position.UpdatedAt
        };
    }
}
