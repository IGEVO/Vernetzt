using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Igevo.Vernetzt.Infrastructure.Services;

public class ClubTypeService : IClubTypeService
{
    private readonly IClubTypeRepository _clubTypeRepository;
    private readonly IUserRepository _userRepository;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<ClubTypeService> _logger;

    public ClubTypeService(
        IClubTypeRepository clubTypeRepository,
        IUserRepository userRepository,
        IAuditLogRepository auditLogRepository,
        ILogger<ClubTypeService> logger)
    {
        _clubTypeRepository = clubTypeRepository;
        _userRepository = userRepository;
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task<IEnumerable<ClubType>> GetAllActiveAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var allTypes = await _clubTypeRepository.GetAllAsync(cancellationToken);
            return allTypes.OrderBy(ct => ct.Name).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active club types");
            throw;
        }
    }

    public async Task<IEnumerable<ClubType>> GetAllAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
            if (user == null || !(user.Claims.Contains(UserClaim.SystemAdmin) || user.Claims.Contains(UserClaim.Manager)))
            {
                _logger.LogWarning("User {UserId} attempted to get all club types without permission", userId);
                throw new UnauthorizedAccessException("Only system administrators and managers can view all club types.");
            }

            var allTypes = await _clubTypeRepository.GetAllAsync(cancellationToken);
            return allTypes.OrderBy(ct => ct.Name).ToList();
        }
        catch (Exception ex) when (ex is not UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Error getting all club types");
            throw;
        }
    }

    public async Task<ClubType?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _clubTypeRepository.GetByIdAsync(id, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting club type {ClubTypeId}", id);
            throw;
        }
    }

    public async Task<ClubType> CreateAsync(Guid userId, ClubType clubType, CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
            if (user == null || !(user.Claims.Contains(UserClaim.SystemAdmin) || user.Claims.Contains(UserClaim.Manager)))
            {
                _logger.LogWarning("User {UserId} attempted to create club type without permission", userId);
                throw new UnauthorizedAccessException("Only system administrators and managers can create club types.");
            }

            var created = await _clubTypeRepository.AddAsync(clubType, cancellationToken);

            await _auditLogRepository.LogAsync(
                entityType: nameof(ClubType),
                entityId: created.Id,
                action: AuditAction.Create,
                userId: userId,
                oldValues: null,
                newValues: $"Created club type: {created.Name}",
                cancellationToken: cancellationToken);

            _logger.LogInformation("ClubType {ClubTypeId} created by user {UserId}", created.Id, userId);
            return created;
        }
        catch (Exception ex) when (ex is not UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Error creating club type");
            throw;
        }
    }

    public async Task<ClubType?> UpdateAsync(Guid userId, Guid clubTypeId, ClubType clubType, CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
            if (user == null || !(user.Claims.Contains(UserClaim.SystemAdmin) || user.Claims.Contains(UserClaim.Manager)))
            {
                _logger.LogWarning("User {UserId} attempted to update club type without permission", userId);
                return null;
            }

            var existing = await _clubTypeRepository.GetByIdAsync(clubTypeId, cancellationToken);
            if (existing == null)
            {
                _logger.LogWarning("Attempted to update non-existent club type {ClubTypeId}", clubTypeId);
                return null;
            }

            // Update properties on the tracked entity to avoid EF Core tracking conflicts
            existing.Name = clubType.Name;
            existing.Description = clubType.Description;

            var updated = await _clubTypeRepository.UpdateAsync(existing, cancellationToken);

            await _auditLogRepository.LogAsync(
                entityType: nameof(ClubType),
                entityId: updated.Id,
                action: AuditAction.Update,
                userId: userId,
                oldValues: $"Name: {existing.Name}",
                newValues: $"Name: {updated.Name}",
                cancellationToken: cancellationToken);

            _logger.LogInformation("ClubType {ClubTypeId} updated by user {UserId}", updated.Id, userId);
            return updated;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating club type {ClubTypeId}", clubTypeId);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid clubTypeId, CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
            if (user == null || !(user.Claims.Contains(UserClaim.SystemAdmin) || user.Claims.Contains(UserClaim.Manager)))
            {
                _logger.LogWarning("User {UserId} attempted to delete club type without permission", userId);
                return false;
            }

            var existing = await _clubTypeRepository.GetByIdAsync(clubTypeId, cancellationToken);
            if (existing == null)
            {
                _logger.LogWarning("Attempted to delete non-existent club type {ClubTypeId}", clubTypeId);
                return false;
            }

            var result = await _clubTypeRepository.DeleteAsync(clubTypeId, cancellationToken);

            if (result)
            {
                await _auditLogRepository.LogAsync(
                    entityType: nameof(ClubType),
                    entityId: clubTypeId,
                    action: AuditAction.Delete,
                    userId: userId,
                    oldValues: $"Deleted club type: {existing.Name}",
                    newValues: null,
                    cancellationToken: cancellationToken);

                _logger.LogInformation("ClubType {ClubTypeId} hard deleted by user {UserId}", clubTypeId, userId);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting club type {ClubTypeId}", clubTypeId);
            throw;
        }
    }

    private async Task<bool> IsSystemAdminAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        return user?.Claims.Contains(UserClaim.SystemAdmin) ?? false;
    }
}
