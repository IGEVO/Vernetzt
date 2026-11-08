using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Igevo.Vernetzt.Infrastructure.Services;

public class DepartmentService : IDepartmentService
{
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IClubRepository _clubRepository;
    private readonly IUserRepository _userRepository;
    private readonly IAuthorizationService _authorizationService;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IEmailService _emailService;
    private readonly IAvatarService _avatarService;
    private readonly VernetztDbContext _dbContext;
    private readonly ILogger<DepartmentService> _logger;

    public DepartmentService(
        IDepartmentRepository departmentRepository,
        IClubRepository clubRepository,
        IUserRepository userRepository,
        IAuthorizationService authorizationService,
        IAuditLogRepository auditLogRepository,
        IEmailService emailService,
        IAvatarService avatarService,
        VernetztDbContext dbContext,
        ILogger<DepartmentService> logger)
    {
        _departmentRepository = departmentRepository;
        _clubRepository = clubRepository;
        _userRepository = userRepository;
        _authorizationService = authorizationService;
        _auditLogRepository = auditLogRepository;
        _emailService = emailService;
        _avatarService = avatarService;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<IEnumerable<Department>> GetAccessibleDepartmentsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var accessibleIds = await _authorizationService.GetAccessibleDepartmentIdsAsync(userId, cancellationToken);
            var allDepartments = await _departmentRepository.GetAllAsync(cancellationToken);
            return allDepartments.Where(d => accessibleIds.Contains(d.Id)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting accessible departments for user {UserId}", userId);
            throw;
        }
    }

    public async Task<IEnumerable<Department>> GetByClubIdAsync(Guid userId, Guid clubId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if user can access the club
            if (!await _authorizationService.CanAccessClubAsync(userId, clubId, cancellationToken))
            {
                _logger.LogWarning("User {UserId} attempted to access departments for club {ClubId} without permission", userId, clubId);
                return Enumerable.Empty<Department>();
            }

            var accessibleIds = await _authorizationService.GetAccessibleDepartmentIdsAsync(userId, cancellationToken);
            var allDepartments = await _departmentRepository.GetAllAsync(cancellationToken);
            return allDepartments.Where(d => d.ClubId == clubId && accessibleIds.Contains(d.Id)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting departments for club {ClubId}", clubId);
            throw;
        }
    }

    public async Task<Department?> GetByIdAsync(Guid userId, Guid departmentId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await _authorizationService.CanAccessDepartmentAsync(userId, departmentId, cancellationToken))
            {
                _logger.LogWarning("User {UserId} attempted to access department {DepartmentId} without permission", userId, departmentId);
                return null;
            }

            return await _departmentRepository.GetByIdAsync(departmentId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting department {DepartmentId}", departmentId);
            throw;
        }
    }

    public async Task<Department> CreateAsync(Guid userId, Department department, CancellationToken cancellationToken = default)
    {
        try
        {
            if (department.ParentDepartmentId.HasValue)
            {
                // Groups: editors of the parent department (or of the club) may create them.
                var parent = await _departmentRepository.GetByIdAsync(department.ParentDepartmentId.Value, cancellationToken);
                if (parent == null || parent.IsGroup || parent.ClubId != department.ClubId)
                {
                    throw new InvalidOperationException("Eine Gruppe kann nur unterhalb einer Abteilung desselben Vereins angelegt werden.");
                }

                if (!await _authorizationService.CanEditDepartmentAsync(userId, parent.Id, cancellationToken))
                {
                    _logger.LogWarning("User {UserId} attempted to create group below department {DepartmentId} without permission", userId, parent.Id);
                    throw new UnauthorizedAccessException("You do not have permission to create groups for this department.");
                }
            }
            else if (!await _authorizationService.CanEditClubAsync(userId, department.ClubId, cancellationToken))
            {
                _logger.LogWarning("User {UserId} attempted to create department for club {ClubId} without permission", userId, department.ClubId);
                throw new UnauthorizedAccessException("You do not have permission to create departments for this club.");
            }

            // Verify club exists
            var club = await _clubRepository.GetByIdAsync(department.ClubId, cancellationToken);
            if (club == null)
            {
                _logger.LogWarning("Attempted to create department for non-existent club {ClubId}", department.ClubId);
                throw new InvalidOperationException($"Club with ID {department.ClubId} not found.");
            }

            // Trim string inputs
            department.Name = department.Name?.Trim() ?? string.Empty;
            department.ShortName = TextNormalization.TrimToNull(department.ShortName);
            department.Description = TextNormalization.TrimToNull(department.Description);
            department.Street = department.Street?.Trim();
            department.PostalCode = department.PostalCode?.Trim();
            department.City = department.City?.Trim();
            department.Email = department.Email?.Trim();
            department.WebsiteUrl = department.WebsiteUrl?.Trim();
            department.SocialMediaLinks = SocialMediaLink.Normalize(department.SocialMediaLinks);

            var created = await _departmentRepository.AddAsync(department, cancellationToken);

            await _auditLogRepository.LogAsync(
                entityType: nameof(Department),
                entityId: created.Id,
                action: AuditAction.Create,
                userId: userId,
                oldValues: null,
                newValues: created.IsGroup
                    ? $"Created group: {created.Name} (ParentDepartmentId: {created.ParentDepartmentId})"
                    : $"Created department: {created.Name} (ClubId: {created.ClubId})",
                cancellationToken: cancellationToken);

            _logger.LogInformation("Department {DepartmentId} created by user {UserId}", created.Id, userId);
            return created;
        }
        catch (Exception ex) when (ex is not UnauthorizedAccessException and not InvalidOperationException)
        {
            _logger.LogError(ex, "Error creating department for club {ClubId}", department.ClubId);
            throw;
        }
    }

    public async Task<Department?> UpdateAsync(Guid userId, Guid departmentId, Department department, CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if user can edit the department
            if (!await _authorizationService.CanEditDepartmentAsync(userId, departmentId, cancellationToken))
            {
                _logger.LogWarning("User {UserId} attempted to update department {DepartmentId} without permission", userId, departmentId);
                return null;
            }

            var existing = await _departmentRepository.GetByIdAsync(departmentId, cancellationToken);
            if (existing == null)
            {
                _logger.LogWarning("Attempted to update non-existent department {DepartmentId}", departmentId);
                return null;
            }

            var oldName = existing.Name;

            // Update properties on the tracked entity to avoid EF Core tracking conflicts (trim string inputs)
            existing.Name = department.Name?.Trim() ?? string.Empty;
            existing.ShortName = TextNormalization.TrimToNull(department.ShortName);
            existing.Description = TextNormalization.TrimToNull(department.Description);
            existing.Street = department.Street?.Trim();
            existing.PostalCode = department.PostalCode?.Trim();
            existing.City = department.City?.Trim();
            existing.WebsiteUrl = department.WebsiteUrl?.Trim();
            existing.SocialMediaLinks = SocialMediaLink.Normalize(department.SocialMediaLinks);
            existing.Email = department.Email?.Trim();
            existing.ContactPersonsVisibility = department.ContactPersonsVisibility;

            var updated = await _departmentRepository.UpdateAsync(existing, cancellationToken);

            await _auditLogRepository.LogAsync(
                entityType: nameof(Department),
                entityId: updated.Id,
                action: AuditAction.Update,
                userId: userId,
                oldValues: $"Name: {oldName}",
                newValues: $"Name: {updated.Name}",
                cancellationToken: cancellationToken);

            _logger.LogInformation("Department {DepartmentId} updated by user {UserId}", updated.Id, userId);
            return updated;
        }
        catch (Exception ex) when (ex is not UnauthorizedAccessException and not InvalidOperationException)
        {
            _logger.LogError(ex, "Error updating department {DepartmentId}", departmentId);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid departmentId, CancellationToken cancellationToken = default)
    {
        try
        {
            var existing = await _departmentRepository.GetByIdAsync(departmentId, cancellationToken);
            if (existing == null)
            {
                _logger.LogWarning("Attempted to delete non-existent department {DepartmentId}", departmentId);
                return false;
            }

            if (!await CanDeleteAsync(userId, existing, cancellationToken))
            {
                _logger.LogWarning("User {UserId} attempted to delete department {DepartmentId} without permission", userId, departmentId);
                return false;
            }

            // Groups have no database cascade on their parent: remove them (with their events and contacts) first.
            var groupIds = existing.Groups.Select(g => g.Id).ToList();
            foreach (var groupId in groupIds)
            {
                await _departmentRepository.DeleteAsync(groupId, cancellationToken);
            }

            var result = await _departmentRepository.DeleteAsync(departmentId, cancellationToken);

            if (result)
            {
                await _auditLogRepository.LogAsync(
                    entityType: nameof(Department),
                    entityId: departmentId,
                    action: AuditAction.Delete,
                    userId: userId,
                    oldValues: existing.IsGroup
                        ? $"Deleted group: {existing.Name} (ParentDepartmentId: {existing.ParentDepartmentId})"
                        : $"Deleted department: {existing.Name} (ClubId: {existing.ClubId}, groups: {groupIds.Count})",
                    newValues: null,
                    cancellationToken: cancellationToken);

                foreach (var groupId in groupIds)
                {
                    await CleanupAvatarAfterDepartmentDeletionAsync(groupId, cancellationToken);
                }

                await CleanupAvatarAfterDepartmentDeletionAsync(departmentId, cancellationToken);

                _logger.LogInformation("Department {DepartmentId} deleted by user {UserId}", departmentId, userId);
            }

            return result;
        }
        catch (Exception ex) when (ex is not UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Error deleting department {DepartmentId}", departmentId);
            throw;
        }
    }

    /// <summary>
    /// Departments may be deleted by club editors (and Manager/SystemAdmin); department editors may edit but not delete
    /// their own department. Groups may be deleted by editors of the parent department; group editors cannot delete their group.
    /// </summary>
    private Task<bool> CanDeleteAsync(Guid userId, Department department, CancellationToken cancellationToken) =>
        department.ParentDepartmentId is { } parentId
            ? _authorizationService.CanEditDepartmentAsync(userId, parentId, cancellationToken)
            : _authorizationService.CanEditClubAsync(userId, department.ClubId, cancellationToken);

    private async Task CleanupAvatarAfterDepartmentDeletionAsync(Guid departmentId, CancellationToken cancellationToken)
    {
        try
        {
            await _avatarService.DeleteDepartmentAvatarAsync(departmentId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Department {DepartmentId} was deleted, but avatar cleanup did not complete successfully",
                departmentId);
        }
    }

    public async Task<bool> AddContactPersonAsync(
        Guid userId,
        Guid departmentId,
        Guid contactPersonId,
        Guid? positionId = null,
        bool canEdit = false,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await _authorizationService.CanEditDepartmentAsync(userId, departmentId, cancellationToken))
            {
                _logger.LogWarning("User {UserId} attempted to add contact person to department {DepartmentId} without permission", userId, departmentId);
                return false;
            }

            var department = await _departmentRepository.GetByIdAsync(departmentId, cancellationToken);
            if (department == null)
            {
                _logger.LogWarning("Attempted to add contact person to non-existent department {DepartmentId}", departmentId);
                return false;
            }

            var contactPerson = await _userRepository.GetByIdAsync(contactPersonId, cancellationToken);
            if (contactPerson == null)
            {
                _logger.LogWarning("Attempted to add non-existent contact person {ContactPersonId} to department {DepartmentId}", contactPersonId, departmentId);
                return false;
            }

            if (department.ContactAssignments.Any(ca => ca.UserId == contactPersonId))
            {
                _logger.LogInformation("Contact person {ContactPersonId} already assigned to department {DepartmentId}", contactPersonId, departmentId);
                return false;
            }

            department.ContactAssignments.Add(new ContactAssignment
            {
                UserId = contactPersonId,
                DepartmentId = departmentId,
                ContactPositionId = positionId,
                CanEdit = canEdit,
                AssignedAt = DateTime.UtcNow,
                Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
            });
            await _departmentRepository.UpdateAsync(department, cancellationToken);

            await _auditLogRepository.LogAsync(
                entityType: nameof(Department),
                entityId: departmentId,
                action: AuditAction.Update,
                userId: userId,
                oldValues: $"ContactPerson {contactPersonId} not assigned",
                newValues: $"ContactPerson {contactPersonId} added",
                cancellationToken: cancellationToken);

            // Send email notification to contact person
            try
            {
                // Get club name for the notification
                var club = await _clubRepository.GetByIdAsync(department.ClubId, cancellationToken);
                var clubName = club?.Name ?? "Unbekannter Verein";

                await _emailService.SendDepartmentAssignmentNotificationAsync(
                    contactPerson.Email,
                    contactPerson.FullName,
                    department.Name,
                    clubName,
                    departmentId,
                    canEdit,
                    department.ParentDepartment?.Name,
                    cancellationToken);
            }
            catch (Exception emailEx)
            {
                _logger.LogError(emailEx, "Failed to send department assignment notification email to {Email}", EmailAddressHelper.Mask(contactPerson.Email));
                // Don't fail the assignment if email fails
            }

            _logger.LogInformation("Contact person {ContactPersonId} added to department {DepartmentId} by user {UserId}", contactPersonId, departmentId, userId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding contact person {ContactPersonId} to department {DepartmentId}", contactPersonId, departmentId);
            throw;
        }
    }

    public async Task<bool> UpdateContactPersonAsync(
        Guid userId,
        Guid departmentId,
        Guid contactPersonId,
        Guid? positionId,
        bool canEdit = false,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await _authorizationService.CanEditDepartmentAsync(userId, departmentId, cancellationToken))
            {
                _logger.LogWarning("User {UserId} attempted to update contact position in department {DepartmentId} without permission", userId, departmentId);
                return false;
            }

            var department = await _departmentRepository.GetByIdAsync(departmentId, cancellationToken);
            if (department == null)
            {
                _logger.LogWarning("Attempted to update contact position in non-existent department {DepartmentId}", departmentId);
                return false;
            }

            var assignment = department.ContactAssignments.FirstOrDefault(ca => ca.UserId == contactPersonId);
            if (assignment == null)
            {
                _logger.LogWarning("Contact person {ContactPersonId} not assigned to department {DepartmentId}", contactPersonId, departmentId);
                return false;
            }

            assignment.ContactPositionId = positionId;
            assignment.CanEdit = canEdit;
            assignment.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
            await _departmentRepository.UpdateAsync(department, cancellationToken);

            await _auditLogRepository.LogAsync(
                entityType: nameof(Department),
                entityId: departmentId,
                action: AuditAction.Update,
                userId: userId,
                oldValues: null,
                newValues: $"Updated contact position for {contactPersonId}",
                cancellationToken: cancellationToken);

            _logger.LogInformation("Contact position updated for {ContactPersonId} in department {DepartmentId} by user {UserId}", contactPersonId, departmentId, userId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating contact position {ContactPersonId} in department {DepartmentId}", contactPersonId, departmentId);
            throw;
        }
    }

    public async Task<bool> RemoveContactPersonAsync(Guid userId, Guid departmentId, Guid contactPersonId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await _authorizationService.CanEditDepartmentAsync(userId, departmentId, cancellationToken))
            {
                _logger.LogWarning("User {UserId} attempted to remove contact person from department {DepartmentId} without permission", userId, departmentId);
                return false;
            }

            var department = await _departmentRepository.GetByIdAsync(departmentId, cancellationToken);
            if (department == null)
            {
                _logger.LogWarning("Attempted to remove contact person from non-existent department {DepartmentId}", departmentId);
                return false;
            }

            var assignment = department.ContactAssignments.FirstOrDefault(ca => ca.UserId == contactPersonId);
            if (assignment == null)
            {
                _logger.LogInformation("Contact person {ContactPersonId} not assigned to department {DepartmentId}", contactPersonId, departmentId);
                return false;
            }

            department.ContactAssignments.Remove(assignment);
            _dbContext.Remove(assignment);
            await _departmentRepository.UpdateAsync(department, cancellationToken);

            await _auditLogRepository.LogAsync(
                entityType: nameof(Department),
                entityId: departmentId,
                action: AuditAction.Update,
                userId: userId,
                oldValues: $"ContactPerson {contactPersonId} assigned",
                newValues: $"ContactPerson {contactPersonId} removed",
                cancellationToken: cancellationToken);

            _logger.LogInformation("Contact person {ContactPersonId} removed from department {DepartmentId} by user {UserId}", contactPersonId, departmentId, userId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing contact person {ContactPersonId} from department {DepartmentId}", contactPersonId, departmentId);
            throw;
        }
    }
}
