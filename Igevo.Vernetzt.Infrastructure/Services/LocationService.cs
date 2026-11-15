using System.Text.Json;
using Igevo.Vernetzt.Core.DTOs.Locations;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Igevo.Vernetzt.Infrastructure.Services;

/// <summary>
/// Central list of locations ("Standorte"). Authorization (SystemAdmin, Manager) is enforced by the controller.
/// </summary>
public sealed class LocationService(VernetztDbContext context, IAuditLogRepository auditLog) : ILocationService
{
    public async Task<IReadOnlyList<LocationDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await context.Locations
            .AsNoTracking()
            .OrderBy(l => l.Name)
            .Select(l => new LocationDto
            {
                Id = l.Id,
                Name = l.Name,
                Description = l.Description,
                Street = l.Street,
                PostalCode = l.PostalCode,
                City = l.City,
                UsageCount = l.Clubs.Count + l.Departments.Count,
                CreatedAt = l.CreatedAt,
                UpdatedAt = l.UpdatedAt
            })
            .ToListAsync(cancellationToken);

    public async Task<LocationDto> CreateAsync(Guid userId, SaveLocationDto dto, CancellationToken cancellationToken = default)
    {
        var location = new Location();
        await ApplyAsync(location, dto, cancellationToken);
        context.Locations.Add(location);
        await SaveAsync(cancellationToken);

        await auditLog.LogAsync(nameof(Location), location.Id, AuditAction.Create, userId, null, Describe(location), cancellationToken);
        return await GetDtoAsync(location.Id, cancellationToken);
    }

    public async Task<LocationDto> UpdateAsync(Guid userId, Guid id, SaveLocationDto dto, CancellationToken cancellationToken = default)
    {
        var location = await FindAsync(id, cancellationToken);
        var before = Describe(location);
        await ApplyAsync(location, dto, cancellationToken);
        await SaveAsync(cancellationToken);

        await auditLog.LogAsync(nameof(Location), location.Id, AuditAction.Update, userId, before, Describe(location), cancellationToken);
        return await GetDtoAsync(location.Id, cancellationToken);
    }

    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken cancellationToken = default)
    {
        var location = await FindAsync(id, cancellationToken);
        var before = Describe(location);
        context.Locations.Remove(location);
        await context.SaveChangesAsync(cancellationToken);

        await auditLog.LogAsync(nameof(Location), id, AuditAction.Delete, userId, before, null, cancellationToken);
    }

    private async Task<Location> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Locations.FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException();

    private async Task<LocationDto> GetDtoAsync(Guid id, CancellationToken cancellationToken) =>
        (await GetAllAsync(cancellationToken)).First(l => l.Id == id);

    private async Task ApplyAsync(Location location, SaveLocationDto dto, CancellationToken cancellationToken)
    {
        var name = dto.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            throw new InvalidOperationException("Bitte einen Namen angeben.");
        }

        if (name.Length > Location.MaxNameLength)
        {
            throw new InvalidOperationException($"Der Name darf höchstens {Location.MaxNameLength} Zeichen lang sein.");
        }

        // Name column uses NOCASE collation, so this comparison is case-insensitive in SQLite.
        var duplicate = await context.Locations
            .AnyAsync(l => l.Id != location.Id && l.Name == name, cancellationToken);
        if (duplicate)
        {
            throw new InvalidOperationException(DuplicateMessage(name));
        }

        location.Name = name;
        location.Description = TextNormalization.TrimToNull(dto.Description);
        location.Street = TextNormalization.TrimToNull(dto.Street);
        location.PostalCode = TextNormalization.TrimToNull(dto.PostalCode);
        location.City = TextNormalization.TrimToNull(dto.City);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) == true)
        {
            // Concurrent create with the same name: report like the regular duplicate check.
            throw new InvalidOperationException("Ein Standort mit diesem Namen existiert bereits.", ex);
        }
    }

    private static string DuplicateMessage(string name) => $"Ein Standort mit dem Namen „{name}“ existiert bereits.";

    private static string Describe(Location location) =>
        JsonSerializer.Serialize(new { location.Name, location.Description, location.Street, location.PostalCode, location.City });
}
