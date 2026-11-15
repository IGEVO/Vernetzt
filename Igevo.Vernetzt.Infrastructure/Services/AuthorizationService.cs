using System.Security.Claims;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Igevo.Vernetzt.Infrastructure.Services;

/// <summary>
/// Service for checking user permissions and access rights.
/// Provides real-time authorization checks based on current user-club-department associations.
///
/// Permission hierarchy:
/// - SystemAdmin: Full access to everything
/// - Manager: Can edit everything, create clubs/club types/departments/users
/// - DataReader: Can read all data visible to authenticated users (AllUsers level, but not Internal user fields)
/// - All authenticated users (default): Can edit own data and assigned clubs/departments, 
///   create departments in own clubs, create users or assign users to own clubs/departments,
///   view all clubs/departments and user data with visibility level AllUsers
/// </summary>
public class AuthorizationService : IAuthorizationService
{
    private readonly IUserRepository _userRepository;
    private readonly IClubRepository _clubRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuthorizationService> _logger;

    public AuthorizationService(
        IUserRepository userRepository,
        IClubRepository clubRepository,
        IDepartmentRepository departmentRepository,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AuthorizationService> logger)
    {
        _userRepository = userRepository;
        _clubRepository = clubRepository;
        _departmentRepository = departmentRepository;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<bool> IsSystemAdminAsync(CancellationToken cancellationToken = default)
    {
        var userIdClaim = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            _logger.LogWarning("IsSystemAdminAsync failed: No valid user ID in claims");
            return false;
        }

        return await IsSystemAdminAsync(userId, cancellationToken);
    }

    public async Task<bool> IsSystemAdminAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            _logger.LogWarning("IsSystemAdminAsync failed: User {UserId} not found", userId);
            return false;
        }

        return user.Claims.Contains(UserClaim.SystemAdmin);
    }

    public async Task<bool> IsManagerOrAdminAsync(CancellationToken cancellationToken = default)
    {
        var userIdClaim = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            _logger.LogWarning("IsManagerOrAdminAsync failed: No valid user ID in claims");
            return false;
        }

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            _logger.LogWarning("IsManagerOrAdminAsync failed: User {UserId} not found", userId);
            return false;
        }

        return user.Claims.Contains(UserClaim.SystemAdmin) || user.Claims.Contains(UserClaim.Manager);
    }

    public async Task<bool> CanAccessClubAsync(Guid userId, Guid clubId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            _logger.LogWarning("Access check failed: User {UserId} not found", userId);
            return false;
        }

        // SystemAdmin and Manager can access everything
        if (user.Claims.Contains(UserClaim.SystemAdmin) || user.Claims.Contains(UserClaim.Manager))
        {
            return true;
        }

        // DataReader can read all clubs (visibility check happens elsewhere)
        if (user.Claims.Contains(UserClaim.DataReader))
        {
            return true;
        }

        // All authenticated users can view all clubs per requirements
        _logger.LogDebug(
            "Club access check for user {UserId} on club {ClubId}: true (authenticated user)",
            userId,
            clubId);

        return true;
    }

    public async Task<bool> CanAccessDepartmentAsync(Guid userId, Guid departmentId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            _logger.LogWarning("Access check failed: User {UserId} not found", userId);
            return false;
        }

        // SystemAdmin and Manager can access everything
        if (user.Claims.Contains(UserClaim.SystemAdmin) || user.Claims.Contains(UserClaim.Manager))
        {
            return true;
        }

        // DataReader can read all departments
        if (user.Claims.Contains(UserClaim.DataReader))
        {
            return true;
        }

        // All authenticated users can view all departments per requirements
        _logger.LogDebug(
            "Department access check for user {UserId} on department {DepartmentId}: true (authenticated user)",
            userId,
            departmentId);

        return true;
    }

    public async Task<bool> CanEditClubAsync(Guid userId, Guid clubId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            _logger.LogWarning("Edit check failed: User {UserId} not found", userId);
            return false;
        }

        // SystemAdmin and Manager can edit everything
        if (user.Claims.Contains(UserClaim.SystemAdmin) || user.Claims.Contains(UserClaim.Manager))
        {
            return true;
        }

        // DataReader cannot edit (read-only)
        if (user.Claims.Contains(UserClaim.DataReader))
        {
            _logger.LogDebug(
                "Edit denied for DataReader user {UserId} on club {ClubId}",
                userId,
                clubId);
            return false;
        }

        // Load club to check if user is a contact person
        var club = await _clubRepository.GetByIdAsync(clubId, cancellationToken);
        if (club == null)
        {
            _logger.LogWarning("Club {ClubId} not found", clubId);
            return false;
        }

        // User can edit clubs where they are assigned as contact person with CanEdit rights
        if (!club.ContactAssignments.Any(ca => ca.UserId == userId && ca.CanEdit && ca.ConfirmedAt != null))
        {
            _logger.LogDebug(
                "Edit denied for user {UserId} on club {ClubId}: Not a contact person with edit rights",
                userId,
                clubId);
            return false;
        }

        _logger.LogDebug(
            "Club edit check for user {UserId} on club {ClubId}: true (contact person)",
            userId,
            clubId);

        return true;
    }

    public async Task<bool> CanEditDepartmentAsync(Guid userId, Guid departmentId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            _logger.LogWarning("Edit check failed: User {UserId} not found", userId);
            return false;
        }

        // SystemAdmin and Manager can edit everything
        if (user.Claims.Contains(UserClaim.SystemAdmin) || user.Claims.Contains(UserClaim.Manager))
        {
            return true;
        }

        // DataReader cannot edit (read-only)
        if (user.Claims.Contains(UserClaim.DataReader))
        {
            _logger.LogDebug(
                "Edit denied for DataReader user {UserId} on department {DepartmentId}",
                userId,
                departmentId);
            return false;
        }

        // Load department to verify it exists
        var department = await _departmentRepository.GetByIdAsync(departmentId, cancellationToken);
        if (department == null)
        {
            _logger.LogWarning("Department {DepartmentId} not found", departmentId);
            return false;
        }

        // Check if user has direct department assignment with CanEdit rights
        if (department.ContactAssignments.Any(ca => ca.UserId == userId && ca.CanEdit && ca.ConfirmedAt != null))
        {
            _logger.LogDebug(
                "Department edit granted for user {UserId} on department {DepartmentId}: Direct contact person with edit rights",
                userId,
                departmentId);
            return true;
        }

        // Groups: editors of the parent department may edit all groups below it (rights inherit downwards only)
        if (department.ParentDepartmentId.HasValue)
        {
            var parent = department.ParentDepartment
                ?? await _departmentRepository.GetByIdAsync(department.ParentDepartmentId.Value, cancellationToken);
            if (parent != null && parent.ContactAssignments.Any(ca => ca.UserId == userId && ca.CanEdit && ca.ConfirmedAt != null))
            {
                _logger.LogDebug(
                    "Group edit granted for user {UserId} on group {DepartmentId} via parent department contact person",
                    userId,
                    departmentId);
                return true;
            }
        }

        // Check if user has access via parent club with CanEdit rights
        var club = await _clubRepository.GetByIdAsync(department.ClubId, cancellationToken);
        if (club != null && club.ContactAssignments.Any(ca => ca.UserId == userId && ca.CanEdit && ca.ConfirmedAt != null))
        {
            _logger.LogDebug(
                "Department edit granted for user {UserId} on department {DepartmentId} via club contact person",
                userId,
                departmentId);
            return true;
        }

        _logger.LogDebug(
            "Department edit check for user {UserId} on department {DepartmentId}: false",
            userId,
            departmentId);

        return false;
    }

    public async Task<IEnumerable<Guid>> GetAccessibleClubIdsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            _logger.LogWarning("GetAccessibleClubIds failed: User {UserId} not found", userId);
            return Enumerable.Empty<Guid>();
        }

        // SystemAdmin, Manager, and DataReader can access all clubs
        if (user.Claims.Contains(UserClaim.SystemAdmin) ||
            user.Claims.Contains(UserClaim.Manager) ||
            user.Claims.Contains(UserClaim.DataReader))
        {
            var allClubs = await _clubRepository.GetAllAsync(cancellationToken);
            return allClubs.Select(c => c.Id);
        }

        // Regular users can see all clubs per requirements
        var clubs = await _clubRepository.GetAllAsync(cancellationToken);
        return clubs.Select(c => c.Id);
    }

    public async Task<IEnumerable<Guid>> GetAccessibleDepartmentIdsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            _logger.LogWarning("GetAccessibleDepartmentIds failed: User {UserId} not found", userId);
            return Enumerable.Empty<Guid>();
        }

        // SystemAdmin, Manager, and DataReader can access all departments
        if (user.Claims.Contains(UserClaim.SystemAdmin) ||
            user.Claims.Contains(UserClaim.Manager) ||
            user.Claims.Contains(UserClaim.DataReader))
        {
            var allDepartments = await _departmentRepository.GetAllAsync(cancellationToken);
            return allDepartments.Select(d => d.Id);
        }

        // Regular users can see all departments per requirements
        var departments = await _departmentRepository.GetAllAsync(cancellationToken);
        return departments.Select(d => d.Id);
    }
}
