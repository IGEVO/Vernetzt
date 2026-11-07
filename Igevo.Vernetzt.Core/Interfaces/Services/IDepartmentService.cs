using Igevo.Vernetzt.Core.Entities;

namespace Igevo.Vernetzt.Core.Interfaces.Services;

/// <summary>
/// Service for managing departments.
/// </summary>
public interface IDepartmentService
{
    /// <summary>
    /// Gets all departments accessible to the user.
    /// </summary>
    Task<IEnumerable<Department>> GetAccessibleDepartmentsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets departments for a specific club.
    /// </summary>
    Task<IEnumerable<Department>> GetByClubIdAsync(Guid userId, Guid clubId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a department by ID if user has access.
    /// </summary>
    Task<Department?> GetByIdAsync(Guid userId, Guid departmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new department.
    /// </summary>
    Task<Department> CreateAsync(Guid userId, Department department, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing department if user has edit permission.
    /// </summary>
    Task<Department?> UpdateAsync(Guid userId, Guid departmentId, Department department, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a department.
    /// </summary>
    Task<bool> DeleteAsync(Guid userId, Guid departmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a contact person to a department.
    /// </summary>
    /// <summary>
    /// Add a contact person to a department with optional position.
    /// </summary>
    Task<bool> AddContactPersonAsync(
        Guid userId,
        Guid departmentId,
        Guid contactPersonId,
        Guid? positionId = null,
        bool canEdit = false,
        string? notes = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the position and edit rights of a contact person.
    /// </summary>
    Task<bool> UpdateContactPersonAsync(
        Guid userId,
        Guid departmentId,
        Guid contactPersonId,
        Guid? positionId,
        bool canEdit = false,
        string? notes = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a contact person from a department.
    /// </summary>
    Task<bool> RemoveContactPersonAsync(Guid userId, Guid departmentId, Guid contactPersonId, CancellationToken cancellationToken = default);
}
