using System.Security.Claims;
using Igevo.Vernetzt.Core.DTOs.Clubs;
using Igevo.Vernetzt.Core.DTOs.Common;
using Igevo.Vernetzt.Core.DTOs.ContactPositions;
using Igevo.Vernetzt.Core.DTOs.Departments;
using Igevo.Vernetzt.Core.DTOs.Locations;
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
            Locations = MapLocations(club.Locations),
            Email = club.Email,
            ContactAssignments = await MapContactAssignmentsAsync(club.ContactAssignments, canEdit, cancellationToken),
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
            Locations = MapLocations(department.Locations),
            ContactAssignments = await MapContactAssignmentsAsync(department.ContactAssignments, canEdit, cancellationToken),
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
            ContactPersonsCount = await CountVisibleAssignmentsAsync(department.ContactAssignments, canEdit, cancellationToken),
            CanEdit = canEdit,
            HasAvatar = department.HasAvatar
        };
    }

    private async Task<AssignmentViewer> GetViewerAsync(bool canEditEntity, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var currentUser = userId.HasValue ? await _userRepository.GetByIdAsync(userId.Value, cancellationToken) : null;
        return AssignmentViewer.For(currentUser, canEditEntity);
    }

    private async Task<int> CountVisibleAssignmentsAsync(
        IEnumerable<ContactAssignment> assignments, bool canEditEntity, CancellationToken cancellationToken)
    {
        var viewer = await GetViewerAsync(canEditEntity, cancellationToken);
        return assignments.Count(a => AssignmentVisibility.CanSeeAssignment(a, viewer));
    }

    /// <summary>
    /// Map ContactAssignments with visibility filtering. Each assigned user decides per assignment who may
    /// see it; pending assignments are only listed for the user, administration and editors of the entity.
    /// </summary>
    private async Task<List<ContactAssignmentDto>> MapContactAssignmentsAsync(
        IEnumerable<ContactAssignment> assignments,
        bool canEditEntity,
        CancellationToken cancellationToken = default)
    {
        var viewer = await GetViewerAsync(canEditEntity, cancellationToken);
        var result = new List<ContactAssignmentDto>();

        foreach (var assignment in assignments)
        {
            if (!AssignmentVisibility.CanSeeAssignment(assignment, viewer))
            {
                continue;
            }

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
                PhoneNumber = AssignmentVisibility.CanSeeField(assignment, user.PhoneVisibility, viewer) ? user.PhoneNumber : null,
                ContactPositionId = assignment.ContactPositionId,
                PositionName = position?.Name,
                CanEdit = assignment.CanEdit,
                ReceivesLocationInfo = assignment.ReceivesLocationInfo,
                AssignedAt = assignment.AssignedAt,
                AssignmentDetails = assignment.Notes,
                ContextEmail = null,
                ContextEmailVerified = assignment.ContextEmailVerified,
                ContextEmailVerifiedAt = assignment.ContextEmailVerifiedAt,
                Visibility = assignment.Visibility,
                IsConfirmed = assignment.IsConfirmed,
                ConfirmedAt = assignment.ConfirmedAt,
                HasAvatar = user.HasAvatar
            };

            var canViewUserEmail = AssignmentVisibility.CanSeeField(assignment, user.EmailVisibility, viewer);
            var canViewContextByVisibility = AssignmentVisibility.CanSeeContextEmail(assignment, viewer);
            var isOwner = viewer.UserId == user.Id;
            var canViewUnverifiedContext = isOwner || viewer.IsAdminOrManager;

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

    private static List<LocationSummaryDto> MapLocations(IEnumerable<Location>? locations) =>
        (locations ?? [])
            .OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(l => new LocationSummaryDto { Id = l.Id, Name = l.Name })
            .ToList();
}
