using System.Security.Claims;
using Igevo.Vernetzt.Core.DTOs.Clubs;
using Igevo.Vernetzt.Core.DTOs.Common;
using Igevo.Vernetzt.Core.DTOs.ContactPositions;
using Igevo.Vernetzt.Core.DTOs.Departments;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Microsoft.AspNetCore.Http;

namespace Igevo.Vernetzt.Infrastructure.Services;

/// <summary>
/// Helper service for mapping entities to DTOs with permission checks and visibility filtering
/// </summary>
public class DtoMappingService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IUserRepository _userRepository;
    private readonly IContactPositionRepository _contactPositionRepository;
    private readonly IAuthorizationService _authorizationService;

    public DtoMappingService(
        IHttpContextAccessor httpContextAccessor,
        IUserRepository userRepository,
        IContactPositionRepository contactPositionRepository,
        IAuthorizationService authorizationService)
    {
        _httpContextAccessor = httpContextAccessor;
        _userRepository = userRepository;
        _contactPositionRepository = contactPositionRepository;
        _authorizationService = authorizationService;
    }

    /// <summary>
    /// Get current user ID from HTTP context
    /// </summary>
    private Guid? GetCurrentUserId()
    {
        var userIdClaim = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return null;
        }
        return userId;
    }

    /// <summary>
    /// Map Club to ClubDto with permissions for current user
    /// </summary>
    public async Task<ClubDto> MapClubToDtoAsync(Club club, CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        var canEdit = userId.HasValue && await _authorizationService.CanEditClubAsync(userId.Value, club.Id, cancellationToken);
        var isAdmin = userId.HasValue && await _authorizationService.IsSystemAdminAsync(userId.Value, cancellationToken);

        var dto = new ClubDto
        {
            Id = club.Id,
            Name = club.Name,
            ShortName = club.ShortName,
            Description = club.Description,
            ClubTypeId = club.ClubTypeId,
            Street = club.Street,
            PostalCode = club.PostalCode,
            City = club.City,
            WebsiteUrl = club.WebsiteUrl,
            SocialMediaLinks = SocialMediaLinkDto.FromEntities(club.SocialMediaLinks),
            Email = club.Email,
            ContactPersonsVisibility = club.ContactPersonsVisibility,
            ContactAssignments = await MapContactAssignmentsAsync(club.ContactAssignments, club.ContactPersonsVisibility, club.Id, true, cancellationToken),
            Departments = new List<DepartmentSummaryDto>(), // Will be populated by caller if needed
            CanEdit = canEdit,
            CanDelete = isAdmin, // Only system admins can delete clubs
            CanManageContacts = canEdit,
            HasAvatar = club.HasAvatar,
            CreatedAt = club.CreatedAt,
            UpdatedAt = club.UpdatedAt
        };

        return dto;
    }

    /// <summary>
    /// Map Club to ClubSummaryDto for list views
    /// </summary>
    public async Task<ClubSummaryDto> MapClubToSummaryDtoAsync(Club club, CancellationToken cancellationToken = default)
    {
        return new ClubSummaryDto
        {
            Id = club.Id,
            Name = club.Name,
            ClubTypeId = club.ClubTypeId,
            City = club.City,
            WebsiteUrl = club.WebsiteUrl,
            DepartmentCount = 0, // Will be populated by caller from repository
        };
    }

    /// <summary>
    /// Map Department to DepartmentDto with permissions for current user
    /// </summary>
    public async Task<DepartmentDto> MapDepartmentToDtoAsync(Department department, string? clubName = null, CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        var canEdit = userId.HasValue && await _authorizationService.CanEditDepartmentAsync(userId.Value, department.Id, cancellationToken);
        // Same rule as DepartmentService.DeleteAsync: departments by club editors, groups by editors of the parent department
        var canDelete = userId.HasValue && (department.ParentDepartmentId is { } parentId
            ? await _authorizationService.CanEditDepartmentAsync(userId.Value, parentId, cancellationToken)
            : await _authorizationService.CanEditClubAsync(userId.Value, department.ClubId, cancellationToken));

        var groups = new List<DepartmentSummaryDto>();
        foreach (var group in department.Groups.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            groups.Add(await MapDepartmentToSummaryDtoAsync(group, cancellationToken));
        }

        var dto = new DepartmentDto
        {
            Id = department.Id,
            Name = department.Name,
            ShortName = department.ShortName,
            ClubId = department.ClubId,
            ClubName = clubName,
            Description = department.Description,
            ParentDepartmentId = department.ParentDepartmentId,
            ParentDepartmentName = department.ParentDepartment?.Name,
            IsGroup = department.IsGroup,
            Groups = groups,
            Street = department.Street,
            PostalCode = department.PostalCode,
            City = department.City,
            Email = department.Email,
            Website = department.WebsiteUrl,
            SocialMediaLinks = SocialMediaLinkDto.FromEntities(department.SocialMediaLinks),
            ContactPersonsVisibility = department.ContactPersonsVisibility,
            ContactAssignments = await MapContactAssignmentsAsync(department.ContactAssignments, department.ContactPersonsVisibility, department.Id, false, cancellationToken),
            CanEdit = canEdit,
            CanDelete = canDelete,
            CanManageContacts = canEdit,
            HasAvatar = department.HasAvatar,
            CreatedAt = department.CreatedAt,
            UpdatedAt = department.UpdatedAt
        };

        return dto;
    }

    /// <summary>
    /// Map Department to DepartmentSummaryDto for list views
    /// </summary>
    public async Task<DepartmentSummaryDto> MapDepartmentToSummaryDtoAsync(Department department, CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        var canEdit = userId.HasValue && await _authorizationService.CanEditDepartmentAsync(userId.Value, department.Id, cancellationToken);

        return new DepartmentSummaryDto
        {
            Id = department.Id,
            Name = department.Name,
            Description = department.Description,
            ParentDepartmentId = department.ParentDepartmentId,
            GroupCount = department.Groups.Count,
            City = department.City,
            Email = department.Email,
            ContactPersonsCount = department.ContactAssignments.Count,
            CanEdit = canEdit,
            HasAvatar = department.HasAvatar
        };
    }

    /// <summary>
    /// Map ContactAssignments with visibility filtering
    /// </summary>
    private async Task<List<ContactAssignmentDto>> MapContactAssignmentsAsync(
        IEnumerable<ContactAssignment> assignments,
        VisibilityLevel visibility,
        Guid entityId,
        bool isClub,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        var currentUser = userId.HasValue ? await _userRepository.GetByIdAsync(userId.Value, cancellationToken) : null;
        var isAdmin = currentUser?.Claims.Contains(UserClaim.SystemAdmin) ?? false;
        var isManager = currentUser?.Claims.Contains(UserClaim.Manager) ?? false;

        // Check if current user can see contact persons at all (applies to entire list)
        if (!await CanViewContactPersonAsync(visibility, currentUser, isAdmin, isManager, entityId, isClub, cancellationToken))
        {
            return new List<ContactAssignmentDto>(); // Return empty list if user cannot see contact persons
        }

        var result = new List<ContactAssignmentDto>();

        foreach (var assignment in assignments)
        {
            var user = await _userRepository.GetByIdAsync(assignment.UserId, cancellationToken);
            if (user == null)
            {
                continue; // Skip missing users
            }

            var position = assignment.ContactPositionId.HasValue
                ? await _contactPositionRepository.GetByIdAsync(assignment.ContactPositionId.Value, cancellationToken)
                : null;

            var dto = new ContactAssignmentDto
            {
                AssignmentId = assignment.Id,
                UserId = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = null,
                PhoneNumber = await CanViewContactInfoAsync(user.PhoneVisibility, visibility, currentUser, isAdmin, isManager, entityId, isClub, cancellationToken) ? user.PhoneNumber : null,
                ContactPositionId = assignment.ContactPositionId,
                PositionName = position?.Name,
                CanEdit = assignment.CanEdit,
                AssignedAt = assignment.AssignedAt,
                AssignmentDetails = assignment.Notes,
                ContextEmail = null,
                ContextEmailVerified = assignment.ContextEmailVerified,
                ContextEmailVerifiedAt = assignment.ContextEmailVerifiedAt,
                ContextEmailVisibility = assignment.ContextEmailVisibility,
                HasAvatar = user.HasAvatar
            };

            var canViewUserEmail = await CanViewContactInfoAsync(
                user.EmailVisibility, visibility, currentUser, isAdmin, isManager, entityId, isClub, cancellationToken);
            var canViewContextByVisibility = await CanViewContactInfoAsync(
                assignment.ContextEmailVisibility, visibility, currentUser, isAdmin, isManager, entityId, isClub, cancellationToken);
            var isOwner = currentUser?.Id == user.Id;
            var canViewUnverifiedContext = isOwner || isAdmin || isManager;

            if (!string.IsNullOrWhiteSpace(assignment.ContextEmail))
            {
                if (assignment.ContextEmailVerified)
                {
                    if (canViewContextByVisibility)
                    {
                        dto.ContextEmail = assignment.ContextEmail;
                    }
                }
                else if (canViewUnverifiedContext)
                {
                    dto.ContextEmail = assignment.ContextEmail;
                }
            }

            var isContextEmailUsed = assignment.ContextEmailVerified &&
                                     !string.IsNullOrWhiteSpace(assignment.ContextEmail) &&
                                     canViewContextByVisibility;

            if (isContextEmailUsed)
            {
                dto.Email = assignment.ContextEmail;
            }
            else if (canViewUserEmail)
            {
                dto.Email = user.Email;
            }

            var badge = EmailAddressHelper.BuildBadge(
                isContextEmailUsed,
                !string.IsNullOrWhiteSpace(assignment.ContextEmail),
                assignment.ContextEmailVerified);
            dto.EmailBadgeType = badge.Type;
            dto.EmailBadgeLabel = badge.Label;
            dto.EmailBadgeReason = badge.Reason;

            result.Add(dto);
        }

        return result;
    }

    /// <summary>
    /// Check if current user can view contact persons based on visibility level.
    /// All authenticated users can always see all users (at least their names).
    /// Internal level is treated as AllUsers for backward compatibility (legacy data).
    /// </summary>
    private async Task<bool> CanViewContactPersonAsync(
        VisibilityLevel visibility,
        User? currentUser,
        bool isAdmin,
        bool isManager,
        Guid entityId,
        bool isClub,
        CancellationToken cancellationToken)
    {
        // Check visibility level
        switch (visibility)
        {
            case VisibilityLevel.Public:
                return true; // Everyone can see (including non-authenticated)

            case VisibilityLevel.Internal:
            case VisibilityLevel.AllUsers:
                // Internal is treated as AllUsers for ContactPersonsVisibility (backward compatibility)
                return currentUser != null; // Only authenticated users can see

            default:
                return false;
        }
    }

    /// <summary>
    /// Check if contact info (email/phone) should be shown based on user's privacy settings.
    /// Takes the more restrictive of the two visibility levels (container vs. individual field).
    /// All authenticated users can see names, but email/phone depend on user's personal settings.
    /// Internal level is only used for user fields (email/phone), not for ContactPersonsVisibility.
    /// </summary>
    private async Task<bool> CanViewContactInfoAsync(
        VisibilityLevel infoVisibility,
        VisibilityLevel containerVisibility,
        User? currentUser,
        bool isAdmin,
        bool isManager,
        Guid entityId,
        bool isClub,
        CancellationToken cancellationToken)
    {
        // Admins, Managers, and DataReaders can always see everything
        if (isAdmin || isManager)
        {
            return true;
        }

        // Check if user has DataReader claim
        var hasDataReader = currentUser?.Claims.Contains(Core.Enums.UserClaim.DataReader) == true;

        // Take the more restrictive visibility level (higher enum value = more restrictive)
        // Note: Internal (0) < AllUsers (1) < Public (2), so we need Min for most restrictive
        var effectiveVisibility = (VisibilityLevel)Math.Min((int)infoVisibility, (int)containerVisibility);

        // Apply visibility logic
        switch (effectiveVisibility)
        {
            case VisibilityLevel.Internal:
                // Internal: Admins, managers, and DataReaders can see
                return hasDataReader;

            case VisibilityLevel.AllUsers:
                return currentUser != null; // Only authenticated users

            case VisibilityLevel.Public:
                return true; // Everyone can see

            default:
                return false;
        }
    }
}
