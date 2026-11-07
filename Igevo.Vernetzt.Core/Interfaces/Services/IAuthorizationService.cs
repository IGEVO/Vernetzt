namespace Igevo.Vernetzt.Core.Interfaces.Services;

/// <summary>
/// Service for checking user permissions and access rights.
/// </summary>
public interface IAuthorizationService
{
    /// <summary>
    /// Checks if the current user is a system administrator.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if user is system admin, false otherwise.</returns>
    Task<bool> IsSystemAdminAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a specific user is a system administrator.
    /// </summary>
    /// <param name="userId">User ID to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if user is system admin, false otherwise.</returns>
    Task<bool> IsSystemAdminAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the current user is a system administrator or manager.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if user is system admin or manager, false otherwise.</returns>
    Task<bool> IsManagerOrAdminAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the user has access to a specific club.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="clubId">Club ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if user has access, false otherwise.</returns>
    Task<bool> CanAccessClubAsync(Guid userId, Guid clubId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the user has access to a specific department.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="departmentId">Department ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if user has access, false otherwise.</returns>
    Task<bool> CanAccessDepartmentAsync(Guid userId, Guid departmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the user can edit a specific club.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="clubId">Club ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if user can edit, false otherwise.</returns>
    Task<bool> CanEditClubAsync(Guid userId, Guid clubId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the user can edit a specific department.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="departmentId">Department ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if user can edit, false otherwise.</returns>
    Task<bool> CanEditDepartmentAsync(Guid userId, Guid departmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all club IDs the user has access to.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of club IDs.</returns>
    Task<IEnumerable<Guid>> GetAccessibleClubIdsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all department IDs the user has access to.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of department IDs.</returns>
    Task<IEnumerable<Guid>> GetAccessibleDepartmentIdsAsync(Guid userId, CancellationToken cancellationToken = default);
}
