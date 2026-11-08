using System.IO.Compression;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Igevo.Vernetzt.Core.Configuration;
using Igevo.Vernetzt.Core.DTOs.Common;
using Igevo.Vernetzt.Core.DTOs.Gdpr;
using Igevo.Vernetzt.Core.DTOs.Invitations;
using Igevo.Vernetzt.Core.DTOs.Users;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Igevo.Vernetzt.Infrastructure.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly IClubRepository _clubRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IContactPositionRepository _contactPositionRepository;
    private readonly IAuthorizationService _authorizationService;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IEmailService _emailService;
    private readonly IInvitationTokenRepository _invitationTokenRepository;
    private readonly IAppSettingsService _appSettingsService;
    private readonly VernetztDbContext _dbContext;
    private readonly AppSettings _appSettings;
    private const string ProfilePictureMediaType = "ProfilePicture";

    private readonly IAvatarService? _avatarService;
    private readonly ILogger<UserService> _logger;

    public UserService(
        IUserRepository userRepository,
        IClubRepository clubRepository,
        IDepartmentRepository departmentRepository,
        IContactPositionRepository contactPositionRepository,
        IAuthorizationService authorizationService,
        IAuditLogRepository auditLogRepository,
        IEmailService emailService,
        IInvitationTokenRepository invitationTokenRepository,
        IAppSettingsService appSettingsService,
        VernetztDbContext dbContext,
        IOptions<AppSettings> appSettings,
        ILogger<UserService> logger,
        IAvatarService? avatarService = null)
    {
        _userRepository = userRepository;
        _clubRepository = clubRepository;
        _departmentRepository = departmentRepository;
        _contactPositionRepository = contactPositionRepository;
        _authorizationService = authorizationService;
        _auditLogRepository = auditLogRepository;
        _emailService = emailService;
        _invitationTokenRepository = invitationTokenRepository;
        _appSettingsService = appSettingsService;
        _dbContext = dbContext;
        _appSettings = appSettings.Value;
        _avatarService = avatarService;
        _logger = logger;
    }

    public async Task<IEnumerable<User>> GetAllAsync(Guid requestingUserId, CancellationToken cancellationToken = default)
    {
        try
        {
            var requestingUser = await _userRepository.GetByIdAsync(requestingUserId, cancellationToken);
            if (requestingUser == null || !(requestingUser.Claims.Contains(UserClaim.SystemAdmin) || requestingUser.Claims.Contains(UserClaim.Manager)))
            {
                _logger.LogWarning("User {UserId} attempted to get all users without permission", requestingUserId);
                throw new UnauthorizedAccessException("Only system administrators and managers can view all users.");
            }

            return await _userRepository.GetAllAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Error getting all users");
            throw;
        }
    }

    public async Task<User?> GetByIdAsync(Guid requestingUserId, Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var requestingUser = await _userRepository.GetByIdAsync(requestingUserId, cancellationToken);
            if (requestingUser == null)
            {
                return null;
            }

            // Allow users to view themselves, SystemAdmin and Manager can view anyone
            if (requestingUserId != userId &&
                !requestingUser.Claims.Contains(UserClaim.SystemAdmin) &&
                !requestingUser.Claims.Contains(UserClaim.Manager))
            {
                _logger.LogWarning("User {RequestingUserId} attempted to view user {UserId} without permission", requestingUserId, userId);
                return null;
            }

            return await _userRepository.GetByIdAsync(userId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user {UserId}", userId);
            throw;
        }
    }

    public async Task<User?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _userRepository.GetByIdAsync(userId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current user {UserId}", userId);
            throw;
        }
    }

    public async Task<User> CreateAsync(Guid requestingUserId, User user, CancellationToken cancellationToken = default)
    {
        try
        {
            var requestingUser = await _userRepository.GetByIdAsync(requestingUserId, cancellationToken);
            if (requestingUser == null)
            {
                _logger.LogWarning("User {UserId} not found", requestingUserId);
                throw new UnauthorizedAccessException("User not found.");
            }

            // Only SystemAdmin and Manager can create users directly.
            // Club / department editors onboard new users via InviteUserAsync for their own scope.
            var canCreate = requestingUser.Claims.Contains(UserClaim.SystemAdmin) ||
                            requestingUser.Claims.Contains(UserClaim.Manager);

            if (!canCreate)
            {
                _logger.LogWarning("User {UserId} attempted to create user without permission", requestingUserId);
                throw new UnauthorizedAccessException("You do not have permission to create users.");
            }

            // Validate claims: SystemAdmin can assign any claim, Manager can assign all except SystemAdmin
            var creatorIsSystemAdmin = requestingUser.Claims.Contains(UserClaim.SystemAdmin);
            var creatorIsManager = requestingUser.Claims.Contains(UserClaim.Manager);

            if (user.Claims.Contains(UserClaim.SystemAdmin) && !creatorIsSystemAdmin)
            {
                _logger.LogWarning("User {UserId} attempted to create user with SystemAdmin claim without permission", requestingUserId);
                throw new UnauthorizedAccessException("Only system administrators can assign the SystemAdmin claim.");
            }

            if (user.Claims.Contains(UserClaim.Manager) && !creatorIsSystemAdmin && !creatorIsManager)
            {
                _logger.LogWarning("User {UserId} attempted to create user with Manager claim without permission", requestingUserId);
                throw new UnauthorizedAccessException("Only system administrators and managers can assign the Manager claim.");
            }

            var created = await _userRepository.AddAsync(user, cancellationToken);

            await _auditLogRepository.LogAsync(
                entityType: nameof(User),
                entityId: created.Id,
                action: AuditAction.Create,
                userId: requestingUserId,
                oldValues: null,
                newValues: System.Text.Json.JsonSerializer.Serialize(created),
                cancellationToken: cancellationToken);

            // Send email notification to new user
            try
            {
                await _emailService.SendUserCreatedNotificationAsync(
                    created.Email,
                    created.FullName,
                    loginUrl: null,
                    cancellationToken: cancellationToken);
            }
            catch (Exception emailEx)
            {
                _logger.LogError(emailEx, "Failed to send user creation notification email to {Email}", EmailAddressHelper.Mask(created.Email));
                // Don't fail the user creation if email fails
            }

            _logger.LogInformation("User {UserId} created by user {RequestingUserId}", created.Id, requestingUserId);
            return created;
        }
        catch (Exception ex) when (ex is not UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Error creating user");
            throw;
        }
    }

    public async Task<User?> UpdateAsync(Guid requestingUserId, Guid userId, User user, CancellationToken cancellationToken = default)
    {
        try
        {
            var requestingUser = await _userRepository.GetByIdAsync(requestingUserId, cancellationToken);
            if (requestingUser == null)
            {
                return null;
            }

            // Allow users to update themselves, SystemAdmin and Manager can update anyone
            var canUpdate = requestingUserId == userId ||
                            requestingUser.Claims.Contains(UserClaim.SystemAdmin) ||
                            requestingUser.Claims.Contains(UserClaim.Manager);

            if (!canUpdate)
            {
                _logger.LogWarning("User {RequestingUserId} attempted to update user {UserId} without permission", requestingUserId, userId);
                return null;
            }

            var existing = await _userRepository.GetByIdAsync(userId, cancellationToken);
            if (existing == null)
            {
                _logger.LogWarning("Attempted to update non-existent user {UserId}", userId);
                return null;
            }

            // Only a SystemAdmin may edit another SystemAdmin
            if (requestingUserId != userId &&
                existing.Claims.Contains(UserClaim.SystemAdmin) &&
                !requestingUser.Claims.Contains(UserClaim.SystemAdmin))
            {
                _logger.LogWarning("User {RequestingUserId} attempted to update SystemAdmin {UserId} without SystemAdmin rights", requestingUserId, userId);
                return null;
            }

            // Update only the fields that are in UpdateUserDto (trim string inputs)
            existing.FirstName = user.FirstName?.Trim() ?? string.Empty;
            existing.LastName = user.LastName?.Trim() ?? string.Empty;
            existing.PhoneNumber = user.PhoneNumber?.Trim();
            existing.EmailVisibility = user.EmailVisibility;
            existing.PhoneVisibility = user.PhoneVisibility;

            // Regular users cannot modify their own claims
            if (requestingUserId == userId &&
                !requestingUser.Claims.Contains(UserClaim.SystemAdmin) &&
                !requestingUser.Claims.Contains(UserClaim.Manager))
            {
                // Claims are already preserved from existing
            }
            else if (user.Claims.Any())
            {
                // Only update claims if provided and user has permission
                existing.Claims = user.Claims;
            }

            var updated = await _userRepository.UpdateAsync(existing, cancellationToken);

            await _auditLogRepository.LogAsync(
                entityType: nameof(User),
                entityId: updated.Id,
                action: AuditAction.Update,
                userId: requestingUserId,
                oldValues: System.Text.Json.JsonSerializer.Serialize(existing),
                newValues: System.Text.Json.JsonSerializer.Serialize(updated),
                cancellationToken: cancellationToken);

            _logger.LogInformation("User {UserId} updated by user {RequestingUserId}", updated.Id, requestingUserId);
            return updated;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating user {UserId}", userId);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(Guid requestingUserId, Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var requestingUser = await _userRepository.GetByIdAsync(requestingUserId, cancellationToken);
            if (requestingUser == null ||
                !(requestingUser.Claims.Contains(UserClaim.SystemAdmin) || requestingUser.Claims.Contains(UserClaim.Manager)))
            {
                _logger.LogWarning("User {UserId} attempted to delete user without permission", requestingUserId);
                return false;
            }

            var existing = await _userRepository.GetByIdAsync(userId, cancellationToken);
            if (existing == null)
            {
                _logger.LogWarning("Attempted to delete non-existent user {UserId}", userId);
                return false;
            }

            // Prevent deletion of the configured system admin user
            if (existing.Email.Equals(_appSettings.SystemAdminEmail, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Attempted to delete the configured system administrator {Email}. This is not allowed.", EmailAddressHelper.Mask(existing.Email));
                return false;
            }

            if (existing.Claims.Contains(UserClaim.SystemAdmin))
            {
                // Only a SystemAdmin may delete another SystemAdmin
                if (!requestingUser.Claims.Contains(UserClaim.SystemAdmin))
                {
                    _logger.LogWarning("User {UserId} attempted to delete SystemAdmin {TargetId} without SystemAdmin rights", requestingUserId, userId);
                    return false;
                }

                // Prevent deleting the last system admin
                var adminCount = (await _userRepository.GetAllAsync(cancellationToken))
                    .Count(u => u.Claims.Contains(UserClaim.SystemAdmin));
                if (adminCount <= 1)
                {
                    _logger.LogWarning("Attempted to delete the last system administrator {UserId}", userId);
                    return false;
                }
            }

            await _auditLogRepository.LogAsync(
                entityType: nameof(User),
                entityId: userId,
                action: AuditAction.Delete,
                userId: requestingUserId,
                oldValues: System.Text.Json.JsonSerializer.Serialize(existing),
                newValues: null,
                cancellationToken: cancellationToken);

            // Delete the user (cascade deletes contact assignments)
            await _userRepository.DeleteAsync(userId, cancellationToken);
            await CleanupUserAvatarAsync(userId, cancellationToken);

            _logger.LogInformation("User {UserId} deleted by user {RequestingUserId}", userId, requestingUserId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting user {UserId}", userId);
            throw;
        }
    }

    public async Task<bool> AssignClaimAsync(Guid requestingUserId, Guid userId, UserClaim claim, CancellationToken cancellationToken = default)
    {
        try
        {
            var requestingUser = await _userRepository.GetByIdAsync(requestingUserId, cancellationToken);
            if (requestingUser == null)
            {
                _logger.LogWarning("Requesting user {UserId} not found", requestingUserId);
                return false;
            }

            // Only SystemAdmin and Manager can assign claims
            var canAssignClaims = requestingUser.Claims.Contains(UserClaim.SystemAdmin) ||
                                  requestingUser.Claims.Contains(UserClaim.Manager);

            if (!canAssignClaims)
            {
                _logger.LogWarning("User {UserId} attempted to assign claim without permission", requestingUserId);
                return false;
            }

            // Only SystemAdmin can assign SystemAdmin claim
            // Manager cannot promote someone to SystemAdmin
            if (claim == UserClaim.SystemAdmin && !requestingUser.Claims.Contains(UserClaim.SystemAdmin))
            {
                _logger.LogWarning("User {UserId} (Manager) attempted to assign SystemAdmin claim", requestingUserId);
                return false;
            }

            var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
            if (user == null)
            {
                _logger.LogWarning("Attempted to assign claim to non-existent user {UserId}", userId);
                return false;
            }

            if (user.Claims.Contains(claim))
            {
                _logger.LogInformation("User {UserId} already has claim {Claim}", userId, claim);
                return false;
            }

            user.Claims.Add(claim);
            await _userRepository.UpdateAsync(user, cancellationToken);

            await _auditLogRepository.LogAsync(
                entityType: nameof(User),
                entityId: userId,
                action: AuditAction.Update,
                userId: requestingUserId,
                oldValues: $"Claim {claim} not assigned",
                newValues: $"Claim {claim} assigned",
                cancellationToken: cancellationToken);

            _logger.LogInformation("Claim {Claim} assigned to user {UserId} by {RequestingUserId}", claim, userId, requestingUserId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning claim {Claim} to user {UserId}", claim, userId);
            throw;
        }
    }

    public async Task<bool> RemoveClaimAsync(Guid requestingUserId, Guid userId, UserClaim claim, CancellationToken cancellationToken = default)
    {
        try
        {
            var requestingUser = await _userRepository.GetByIdAsync(requestingUserId, cancellationToken);
            if (requestingUser == null ||
                !(requestingUser.Claims.Contains(UserClaim.SystemAdmin) || requestingUser.Claims.Contains(UserClaim.Manager)))
            {
                _logger.LogWarning("User {UserId} attempted to remove claim without permission", requestingUserId);
                return false;
            }

            // Only SystemAdmin can remove SystemAdmin claim
            if (claim == UserClaim.SystemAdmin && !requestingUser.Claims.Contains(UserClaim.SystemAdmin))
            {
                _logger.LogWarning("User {UserId} (Manager) attempted to remove SystemAdmin claim", requestingUserId);
                return false;
            }

            var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
            if (user == null)
            {
                _logger.LogWarning("Attempted to remove claim from non-existent user {UserId}", userId);
                return false;
            }

            if (!user.Claims.Contains(claim))
            {
                _logger.LogInformation("User {UserId} does not have claim {Claim}", userId, claim);
                return false;
            }

            user.Claims.Remove(claim);
            await _userRepository.UpdateAsync(user, cancellationToken);

            await _auditLogRepository.LogAsync(
                entityType: nameof(User),
                entityId: userId,
                action: AuditAction.Update,
                userId: requestingUserId,
                oldValues: $"Claim {claim} assigned",
                newValues: $"Claim {claim} removed",
                cancellationToken: cancellationToken);

            _logger.LogInformation("Claim {Claim} removed from user {UserId} by {RequestingUserId}", claim, userId, requestingUserId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing claim {Claim} from user {UserId}", claim, userId);
            throw;
        }
    }

    public async Task<IEnumerable<ContactDto>> GetAllContactsAsync(Guid requestingUserId, CancellationToken cancellationToken = default)
    {
        try
        {
            var requestingUser = await _userRepository.GetByIdAsync(requestingUserId, cancellationToken);
            if (requestingUser == null)
            {
                _logger.LogWarning("Invalid user {UserId} attempted to get contacts", requestingUserId);
                throw new UnauthorizedAccessException("User not found.");
            }

            var isAdmin = requestingUser.Claims.Contains(UserClaim.SystemAdmin);
            var isManager = requestingUser.Claims.Contains(UserClaim.Manager);
            var isDataReader = requestingUser.Claims.Contains(UserClaim.DataReader);

            // Get all users
            var users = (await _userRepository.GetAllAsync(cancellationToken))
                .ToList();

            // Get all clubs with contact assignments included
            var clubs = (await _clubRepository.GetAllWithDetailsAsync(cancellationToken)).ToList();

            // Get all departments with contact assignments
            var allDepartments = new List<Department>();
            foreach (var club in clubs)
            {
                var departments = await _departmentRepository.GetByClubIdAsync(club.Id, cancellationToken);
                allDepartments.AddRange(departments);
            }

            // Get all contact positions for mapping
            var positions = (await _contactPositionRepository.GetAllAsync(cancellationToken)).ToList();

            // Build contact DTOs
            var contacts = new List<ContactDto>();

            foreach (var user in users)
            {
                // All authenticated users can see all contacts.
                // The only per-contact distinction is email/phone visibility.

                // Build contact DTO
                var contactDto = new ContactDto
                {
                    Id = user.Id,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    CreatedAt = user.CreatedAt,
                    EmailVisibility = user.EmailVisibility,
                    PhoneVisibility = user.PhoneVisibility,
                    HasAvatar = user.HasAvatar,
                    Organizations = new List<ContactOrganizationDto>()
                };

                // Apply visibility rules for email and phone
                if (CanViewByVisibility(user.EmailVisibility, isAdmin, isManager, isDataReader, user.Id == requestingUserId))
                {
                    contactDto.Email = user.Email;
                }

                if (CanViewByVisibility(user.PhoneVisibility, isAdmin, isManager, isDataReader, user.Id == requestingUserId))
                {
                    contactDto.PhoneNumber = user.PhoneNumber;
                }

                // Add club assignments
                foreach (var club in clubs)
                {
                    var assignment = club.ContactAssignments?.FirstOrDefault(ca => ca.UserId == user.Id);
                    if (assignment != null)
                    {
                        var position = assignment.ContactPositionId.HasValue
                            ? positions.FirstOrDefault(p => p.Id == assignment.ContactPositionId.Value)
                            : null;
                        var canViewContextByVisibility = CanViewByVisibility(
                            assignment.ContextEmailVisibility, isAdmin, isManager, isDataReader, user.Id == requestingUserId);
                        var canViewUnverifiedContext = isAdmin || isManager || user.Id == requestingUserId;
                        var assignmentEmail = !string.IsNullOrWhiteSpace(assignment.ContextEmail) &&
                                              ((assignment.ContextEmailVerified && canViewContextByVisibility) ||
                                               (!assignment.ContextEmailVerified && canViewUnverifiedContext))
                            ? assignment.ContextEmail
                            : null;

                        contactDto.Organizations.Add(new ContactOrganizationDto
                        {
                            OrganizationId = club.Id,
                            OrganizationName = club.Name,
                            OrganizationShortName = club.ShortName,
                            Type = OrganizationType.Club,
                            ParentId = null,
                            ParentName = null,
                            ClubTypeId = club.ClubTypeId,
                            PositionId = assignment.ContactPositionId,
                            PositionName = position?.Name,
                            AssignmentEmail = assignmentEmail,
                            AssignedAt = assignment.AssignedAt
                        });
                    }
                }

                // Add department assignments
                foreach (var dept in allDepartments)
                {
                    var assignment = dept.ContactAssignments?.FirstOrDefault(ca => ca.UserId == user.Id);
                    if (assignment != null)
                    {
                        var parentClub = clubs.FirstOrDefault(c => c.Id == dept.ClubId);
                        var position = assignment.ContactPositionId.HasValue
                            ? positions.FirstOrDefault(p => p.Id == assignment.ContactPositionId.Value)
                            : null;
                        var canViewContextByVisibility = CanViewByVisibility(
                            assignment.ContextEmailVisibility, isAdmin, isManager, isDataReader, user.Id == requestingUserId);
                        var canViewUnverifiedContext = isAdmin || isManager || user.Id == requestingUserId;
                        var assignmentEmail = !string.IsNullOrWhiteSpace(assignment.ContextEmail) &&
                                              ((assignment.ContextEmailVerified && canViewContextByVisibility) ||
                                               (!assignment.ContextEmailVerified && canViewUnverifiedContext))
                            ? assignment.ContextEmail
                            : null;

                        contactDto.Organizations.Add(new ContactOrganizationDto
                        {
                            OrganizationId = dept.Id,
                            OrganizationName = dept.Name,
                            OrganizationShortName = dept.ShortName,
                            Type = OrganizationType.Department,
                            ParentId = dept.ClubId,
                            ParentName = parentClub?.Name,
                            ParentShortName = parentClub?.ShortName,
                            ParentDepartmentId = dept.ParentDepartmentId,
                            ParentDepartmentName = dept.ParentDepartment?.Name,
                            ParentDepartmentShortName = dept.ParentDepartment?.ShortName,
                            ClubTypeId = parentClub?.ClubTypeId,
                            PositionId = assignment.ContactPositionId,
                            PositionName = position?.Name,
                            AssignmentEmail = assignmentEmail,
                            AssignedAt = assignment.AssignedAt
                        });
                    }
                }

                contacts.Add(contactDto);
            }

            _logger.LogInformation("Retrieved {Count} contacts for user {UserId}", contacts.Count, requestingUserId);
            return contacts.OrderBy(c => c.LastName).ThenBy(c => c.FirstName);
        }
        catch (Exception ex) when (ex is not UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Error getting contacts for user {UserId}", requestingUserId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<PagedResult<ContactDto>> SearchContactsAsync(Guid requestingUserId, ContactSearchParameters parameters, CancellationToken cancellationToken = default)
    {
        parameters.Normalize();

        // Reuse existing logic to get all visible contacts with visibility rules applied
        var allContacts = (await GetAllContactsAsync(requestingUserId, cancellationToken)).ToList();

        // Apply text search across name, club, department, and position
        if (!string.IsNullOrWhiteSpace(parameters.SearchTerm))
        {
            var searchLower = parameters.SearchTerm.ToLower();
            allContacts = allContacts.Where(c =>
                c.FirstName.ToLower().Contains(searchLower) ||
                c.LastName.ToLower().Contains(searchLower) ||
                c.Organizations.Any(o =>
                    o.OrganizationName.ToLower().Contains(searchLower) ||
                    (o.ParentName?.ToLower().Contains(searchLower) ?? false) ||
                    (o.PositionName?.ToLower().Contains(searchLower) ?? false))
            ).ToList();
        }

        var totalCount = allContacts.Count;
        var paged = allContacts
            .Skip(parameters.Skip)
            .Take(parameters.PageSize)
            .ToList();

        return PagedResult<ContactDto>.Create(paged, totalCount, parameters.Page, parameters.PageSize);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<RecipientCandidateDto>> GetRecipientCandidatesAsync(Guid requestingUserId, CancellationToken cancellationToken = default)
    {
        var requestingUser = await _userRepository.GetByIdAsync(requestingUserId, cancellationToken);
        if (requestingUser == null)
        {
            throw new UnauthorizedAccessException("User not found.");
        }

        var isAdmin = requestingUser.Claims.Contains(UserClaim.SystemAdmin);
        var isManager = requestingUser.Claims.Contains(UserClaim.Manager);
        var isDataReader = requestingUser.Claims.Contains(UserClaim.DataReader);

        var assignments = await _dbContext.ContactAssignments
            .AsNoTracking()
            .Include(a => a.User)
            .Include(a => a.ContactPosition)
            .Include(a => a.Club)
                .ThenInclude(c => c!.ClubType)
            .Include(a => a.Department)
                .ThenInclude(d => d!.Club)
                    .ThenInclude(c => c!.ClubType)
            .Include(a => a.Department)
                .ThenInclude(d => d!.ParentDepartment)
            .ToListAsync(cancellationToken);

        var result = new List<RecipientCandidateDto>(assignments.Count);
        foreach (var assignment in assignments)
        {
            var canAccessAssignment = assignment.ClubId.HasValue
                ? await _authorizationService.CanAccessClubAsync(requestingUserId, assignment.ClubId.Value, cancellationToken)
                : assignment.DepartmentId.HasValue
                    ? await _authorizationService.CanAccessDepartmentAsync(requestingUserId, assignment.DepartmentId.Value, cancellationToken)
                    : false;

            if (!canAccessAssignment)
            {
                continue;
            }

            var user = assignment.User;
            var canViewPrimaryEmail = CanViewByVisibility(
                user.EmailVisibility, isAdmin, isManager, isDataReader, user.Id == requestingUserId);
            var canViewContextEmailByVisibility = CanViewByVisibility(
                assignment.ContextEmailVisibility, isAdmin, isManager, isDataReader, user.Id == requestingUserId);
            var canViewUnverifiedContext = isAdmin || isManager || user.Id == requestingUserId;

            var isContextEmailUsed = assignment.ContextEmailVerified &&
                                     !string.IsNullOrWhiteSpace(assignment.ContextEmailNormalized);

            string? displayEmail = null;
            if (isContextEmailUsed && canViewContextEmailByVisibility)
            {
                displayEmail = assignment.ContextEmail;
            }
            else if (canViewPrimaryEmail)
            {
                displayEmail = user.Email;
            }
            else if (!assignment.ContextEmailVerified &&
                     !string.IsNullOrWhiteSpace(assignment.ContextEmail) &&
                     canViewUnverifiedContext)
            {
                displayEmail = assignment.ContextEmail;
            }

            var badge = EmailAddressHelper.BuildBadge(
                isContextEmailUsed,
                !string.IsNullOrWhiteSpace(assignment.ContextEmail),
                assignment.ContextEmailVerified);

            var dto = new RecipientCandidateDto
            {
                AssignmentId = assignment.Id,
                IsAssignmentRecipient = true,
                UserId = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                DisplayEmail = displayEmail,
                IsContextEmailUsed = isContextEmailUsed,
                HasContextEmail = !string.IsNullOrWhiteSpace(assignment.ContextEmail),
                ContextEmailVerified = assignment.ContextEmailVerified,
                EmailBadgeType = badge.Type,
                EmailBadgeLabel = badge.Label,
                EmailBadgeReason = badge.Reason,
                PositionId = assignment.ContactPositionId,
                PositionName = assignment.ContactPosition?.Name,
                AssignmentDetails = assignment.Notes,
                AssignedAt = assignment.AssignedAt,
                HasAvatar = user.HasAvatar
            };

            if (assignment.ClubId.HasValue && assignment.Club != null)
            {
                dto.OrganizationId = assignment.ClubId.Value;
                dto.OrganizationName = assignment.Club.Name;
                dto.OrganizationType = OrganizationType.Club;
                dto.ClubTypeId = assignment.Club.ClubTypeId;
            }
            else if (assignment.DepartmentId.HasValue && assignment.Department != null)
            {
                dto.OrganizationId = assignment.DepartmentId.Value;
                dto.OrganizationName = assignment.Department.Name;
                dto.OrganizationType = OrganizationType.Department;
                dto.ParentClubId = assignment.Department.ClubId;
                dto.ParentClubName = assignment.Department.Club?.Name;
                dto.ParentDepartmentId = assignment.Department.ParentDepartmentId;
                dto.ParentDepartmentName = assignment.Department.ParentDepartment?.Name;
                dto.ClubTypeId = assignment.Department.Club?.ClubTypeId;
            }
            else
            {
                continue;
            }

            result.Add(dto);
        }

        var unassignedUsers = await _dbContext.Users
            .AsNoTracking()
            .Where(u => !u.ContactAssignments.Any())
            .ToListAsync(cancellationToken);

        foreach (var user in unassignedUsers)
        {
            var canViewPrimaryEmail = CanViewByVisibility(
                user.EmailVisibility, isAdmin, isManager, isDataReader, user.Id == requestingUserId);

            var badge = EmailAddressHelper.BuildBadge(
                isContextUsed: false,
                hasContextEmail: false,
                contextVerified: false);

            result.Add(new RecipientCandidateDto
            {
                AssignmentId = user.Id,
                IsAssignmentRecipient = false,
                UserId = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                DisplayEmail = canViewPrimaryEmail ? user.Email : null,
                IsContextEmailUsed = false,
                HasContextEmail = false,
                ContextEmailVerified = false,
                EmailBadgeType = badge.Type,
                EmailBadgeLabel = badge.Label,
                EmailBadgeReason = badge.Reason,
                OrganizationId = Guid.Empty,
                OrganizationName = "Ohne Zuordnung",
                OrganizationType = OrganizationType.None,
                AssignedAt = user.CreatedAt,
                HasAvatar = user.HasAvatar
            });
        }

        return result
            .OrderBy(c => c.LastName)
            .ThenBy(c => c.FirstName)
            .ThenBy(c => c.OrganizationName)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IEnumerable<MyContactAssignmentDto>> GetMyContactAssignmentsAsync(Guid requestingUserId, CancellationToken cancellationToken = default)
    {
        return await GetContactAssignmentsForUserAsync(requestingUserId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<MyContactAssignmentDto>> GetUserContactAssignmentsAsync(
        Guid requestingUserId,
        Guid targetUserId,
        CancellationToken cancellationToken = default)
    {
        var canManageUsers = await IsManagerOrAdminUserAsync(requestingUserId, cancellationToken);
        if (!canManageUsers)
        {
            throw new UnauthorizedAccessException("Only system administrators and managers can view assignments for other users.");
        }

        return await GetContactAssignmentsForUserAsync(targetUserId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<UpdateMyContactAssignmentEmailResponse> UpdateMyContactAssignmentEmailAsync(
        Guid requestingUserId,
        Guid assignmentId,
        UpdateMyContactAssignmentEmailDto dto,
        CancellationToken cancellationToken = default)
    {
        return await UpdateContactAssignmentEmailInternalAsync(
            requestingUserId,
            assignmentId,
            a => a.UserId == requestingUserId,
            "owner",
            cancellationToken,
            dto);
    }

    /// <inheritdoc />
    public async Task<UpdateMyContactAssignmentEmailResponse> UpdateUserContactAssignmentEmailAsync(
        Guid requestingUserId,
        Guid targetUserId,
        Guid assignmentId,
        UpdateMyContactAssignmentEmailDto dto,
        CancellationToken cancellationToken = default)
    {
        var canManageUsers = await IsManagerOrAdminUserAsync(requestingUserId, cancellationToken);
        if (!canManageUsers)
        {
            throw new UnauthorizedAccessException("Only system administrators and managers can update assignments for other users.");
        }

        return await UpdateContactAssignmentEmailInternalAsync(
            requestingUserId,
            assignmentId,
            a => a.UserId == targetUserId,
            $"manager/admin for target-user {targetUserId}",
            cancellationToken,
            dto);
    }

    /// <inheritdoc />
    public async Task<UpdateMyContactAssignmentEmailResponse> ResendMyContactAssignmentEmailVerificationAsync(
        Guid requestingUserId,
        Guid assignmentId,
        CancellationToken cancellationToken = default)
    {
        return await ResendContactAssignmentEmailVerificationInternalAsync(
            requestingUserId,
            assignmentId,
            a => a.UserId == requestingUserId,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<UpdateMyContactAssignmentEmailResponse> ResendUserContactAssignmentEmailVerificationAsync(
        Guid requestingUserId,
        Guid targetUserId,
        Guid assignmentId,
        CancellationToken cancellationToken = default)
    {
        var canManageUsers = await IsManagerOrAdminUserAsync(requestingUserId, cancellationToken);
        if (!canManageUsers)
        {
            throw new UnauthorizedAccessException("Only system administrators and managers can resend verification for other users.");
        }

        return await ResendContactAssignmentEmailVerificationInternalAsync(
            requestingUserId,
            assignmentId,
            a => a.UserId == targetUserId,
            cancellationToken);
    }

    private async Task<List<MyContactAssignmentDto>> GetContactAssignmentsForUserAsync(Guid targetUserId, CancellationToken cancellationToken)
    {
        var assignments = await _dbContext.ContactAssignments
            .AsNoTracking()
            .Include(a => a.ContactPosition)
            .Include(a => a.Club)
            .Include(a => a.Department)
                .ThenInclude(d => d!.Club)
            .Include(a => a.Department)
                .ThenInclude(d => d!.ParentDepartment)
            .Where(a => a.UserId == targetUserId)
            .OrderBy(a => a.AssignedAt)
            .ToListAsync(cancellationToken);

        var result = new List<MyContactAssignmentDto>(assignments.Count);
        foreach (var assignment in assignments)
        {
            var isContextEmailUsed = assignment.ContextEmailVerified &&
                                     !string.IsNullOrWhiteSpace(assignment.ContextEmailNormalized);
            var badge = EmailAddressHelper.BuildBadge(
                isContextEmailUsed,
                !string.IsNullOrWhiteSpace(assignment.ContextEmail),
                assignment.ContextEmailVerified);

            var dto = new MyContactAssignmentDto
            {
                AssignmentId = assignment.Id,
                PositionId = assignment.ContactPositionId,
                PositionName = assignment.ContactPosition?.Name,
                AssignmentDetails = assignment.Notes,
                AssignedAt = assignment.AssignedAt,
                ContextEmail = assignment.ContextEmail,
                ContextEmailVerified = assignment.ContextEmailVerified,
                ContextEmailVerifiedAt = assignment.ContextEmailVerifiedAt,
                ContextEmailVisibility = assignment.ContextEmailVisibility,
                EffectiveEmailBadgeType = badge.Type,
                EffectiveEmailBadgeLabel = badge.Label,
                EffectiveEmailBadgeReason = badge.Reason
            };

            if (assignment.ClubId.HasValue && assignment.Club != null)
            {
                dto.OrganizationId = assignment.ClubId.Value;
                dto.OrganizationName = assignment.Club.Name;
                dto.OrganizationType = OrganizationType.Club;
            }
            else if (assignment.DepartmentId.HasValue && assignment.Department != null)
            {
                dto.OrganizationId = assignment.DepartmentId.Value;
                dto.OrganizationName = assignment.Department.Name;
                dto.OrganizationType = OrganizationType.Department;
                dto.ParentClubId = assignment.Department.ClubId;
                dto.ParentClubName = assignment.Department.Club?.Name;
                dto.ParentDepartmentId = assignment.Department.ParentDepartmentId;
                dto.ParentDepartmentName = assignment.Department.ParentDepartment?.Name;
            }
            else
            {
                continue;
            }

            result.Add(dto);
        }

        return result;
    }

    private async Task<UpdateMyContactAssignmentEmailResponse> UpdateContactAssignmentEmailInternalAsync(
        Guid requestingUserId,
        Guid assignmentId,
        Func<ContactAssignment, bool> accessPredicate,
        string actorLabel,
        CancellationToken cancellationToken,
        UpdateMyContactAssignmentEmailDto dto)
    {
        var assignment = await _dbContext.ContactAssignments
            .FirstOrDefaultAsync(a => a.Id == assignmentId, cancellationToken);

        if (assignment == null || !accessPredicate(assignment))
        {
            return new UpdateMyContactAssignmentEmailResponse
            {
                Success = false,
                Message = "Zuordnung nicht gefunden oder keine Berechtigung."
            };
        }

        assignment.ContextEmailVisibility = dto.ContextEmailVisibility;
        var normalized = EmailAddressHelper.Normalize(dto.ContextEmail);
        var trimmed = string.IsNullOrWhiteSpace(dto.ContextEmail) ? null : dto.ContextEmail.Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            assignment.ContextEmail = null;
            assignment.ContextEmailNormalized = null;
            assignment.ContextEmailVerified = false;
            assignment.ContextEmailVerifiedAt = null;
            await _dbContext.SaveChangesAsync(cancellationToken);

            await _auditLogRepository.LogAsync(
                "ContactAssignment",
                assignment.Id,
                AuditAction.Update,
                requestingUserId,
                null,
                $"Context email removed by {actorLabel}.",
                cancellationToken);

            return new UpdateMyContactAssignmentEmailResponse
            {
                Success = true,
                VerificationEmailSent = false,
                Message = "Amtsadresse wurde entfernt."
            };
        }

        var emailChanged = !string.Equals(
            assignment.ContextEmailNormalized,
            normalized,
            StringComparison.OrdinalIgnoreCase);

        if (emailChanged && !string.IsNullOrWhiteSpace(normalized))
        {
            var isPrimaryEmailInUse = await _dbContext.Users
                .AsNoTracking()
                .AnyAsync(u => u.Email == normalized, cancellationToken);

            if (isPrimaryEmailInUse)
            {
                return new UpdateMyContactAssignmentEmailResponse
                {
                    Success = false,
                    VerificationEmailSent = false,
                    Message = "Diese Adresse ist bereits die persönliche Anmeldeadresse eines Benutzerkontos und kann nicht als Amtsadresse genutzt werden."
                };
            }
        }

        assignment.ContextEmail = trimmed;
        assignment.ContextEmailNormalized = normalized;

        if (emailChanged)
        {
            assignment.ContextEmailVerified = false;
            assignment.ContextEmailVerifiedAt = null;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var verificationSent = false;
        if (emailChanged && !string.IsNullOrWhiteSpace(assignment.ContextEmailNormalized))
        {
            verificationSent = await IssueAndSendContextEmailVerificationAsync(
                assignment,
                requestingUserId,
                cancellationToken);
        }

        await _auditLogRepository.LogAsync(
            "ContactAssignment",
            assignment.Id,
            AuditAction.Update,
            requestingUserId,
            null,
            $"Context email updated by {actorLabel}. Visibility={assignment.ContextEmailVisibility}, Changed={emailChanged}",
            cancellationToken);

        return new UpdateMyContactAssignmentEmailResponse
        {
            Success = true,
            VerificationEmailSent = verificationSent,
            Message = emailChanged
                ? "Amtsadresse gespeichert. Bitte bestätige die Adresse über den Link in der Bestätigungs-E-Mail."
                : "Sichtbarkeit gespeichert."
        };
    }

    private async Task<UpdateMyContactAssignmentEmailResponse> ResendContactAssignmentEmailVerificationInternalAsync(
        Guid requestingUserId,
        Guid assignmentId,
        Func<ContactAssignment, bool> accessPredicate,
        CancellationToken cancellationToken)
    {
        var assignment = await _dbContext.ContactAssignments
            .FirstOrDefaultAsync(a => a.Id == assignmentId, cancellationToken);

        if (assignment == null || !accessPredicate(assignment))
        {
            return new UpdateMyContactAssignmentEmailResponse
            {
                Success = false,
                Message = "Zuordnung nicht gefunden oder keine Berechtigung."
            };
        }

        if (string.IsNullOrWhiteSpace(assignment.ContextEmailNormalized))
        {
            return new UpdateMyContactAssignmentEmailResponse
            {
                Success = false,
                Message = "Keine Amtsadresse hinterlegt."
            };
        }

        if (assignment.ContextEmailVerified)
        {
            return new UpdateMyContactAssignmentEmailResponse
            {
                Success = false,
                Message = "Diese Amtsadresse ist bereits bestätigt."
            };
        }

        var sent = await IssueAndSendContextEmailVerificationAsync(assignment, requestingUserId, cancellationToken);
        await _auditLogRepository.LogAsync(
            "ContactAssignment",
            assignment.Id,
            AuditAction.Update,
            requestingUserId,
            null,
            "Context email verification resend requested.",
            cancellationToken);

        return new UpdateMyContactAssignmentEmailResponse
        {
            Success = sent,
            VerificationEmailSent = sent,
            Message = sent
                ? "Verifizierungs-E-Mail wurde erneut versendet."
                : "Verifizierungs-E-Mail konnte nicht versendet werden."
        };
    }

    private async Task<bool> IsManagerOrAdminUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            return false;
        }

        return user.Claims.Contains(UserClaim.SystemAdmin) || user.Claims.Contains(UserClaim.Manager);
    }

    /// <inheritdoc />
    public async Task<ContactAssignmentEmailVerificationResultDto> VerifyContactAssignmentEmailAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new ContactAssignmentEmailVerificationResultDto
            {
                Success = false,
                Title = "Ungültiger Link",
                Message = "Der Bestätigungslink ist ungültig."
            };
        }

        var tokenHash = EmailAddressHelper.Sha256(token.Trim());
        var verificationToken = await _dbContext.ContactAssignmentEmailVerificationTokens
            .Include(t => t.ContactAssignment)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        if (verificationToken == null)
        {
            return new ContactAssignmentEmailVerificationResultDto
            {
                Success = false,
                Title = "Link nicht gefunden",
                Message = "Der Bestätigungslink wurde nicht gefunden oder ist nicht mehr gültig."
            };
        }

        if (verificationToken.IsUsed)
        {
            return new ContactAssignmentEmailVerificationResultDto
            {
                Success = false,
                Title = "Link bereits verwendet",
                Message = "Dieser Bestätigungslink wurde bereits verwendet. Die Amtsadresse ist damit bestätigt."
            };
        }

        if (verificationToken.ExpiresAt < DateTime.UtcNow)
        {
            return new ContactAssignmentEmailVerificationResultDto
            {
                Success = false,
                Title = "Link abgelaufen",
                Message = "Dieser Bestätigungslink ist abgelaufen. Unter „Mein Konto → Zuordnungen“ kannst du eine neue Bestätigungs-E-Mail anfordern."
            };
        }

        var assignment = verificationToken.ContactAssignment;
        if (assignment.ContextEmailNormalized != verificationToken.EmailNormalizedSnapshot)
        {
            return new ContactAssignmentEmailVerificationResultDto
            {
                Success = false,
                Title = "Adresse geändert",
                Message = "Die Amtsadresse wurde inzwischen geändert. Bitte fordere unter „Mein Konto → Zuordnungen“ eine neue Bestätigungs-E-Mail an."
            };
        }

        assignment.ContextEmailVerified = true;
        assignment.ContextEmailVerifiedAt = DateTime.UtcNow;
        verificationToken.IsUsed = true;
        verificationToken.UsedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogRepository.LogAsync(
            "ContactAssignment",
            assignment.Id,
            AuditAction.Update,
            assignment.UserId,
            null,
            "Context email verified.",
            cancellationToken);

        return new ContactAssignmentEmailVerificationResultDto
        {
            Success = true,
            Title = "Amtsadresse bestätigt",
            Message = "Die Amtsadresse wurde bestätigt. Mails zu dieser Zuordnung gehen ab jetzt an diese Adresse."
        };
    }

    private static bool CanViewByVisibility(
        VisibilityLevel visibility,
        bool isAdmin,
        bool isManager,
        bool isDataReader,
        bool isOwner)
    {
        if (isAdmin || isManager || isOwner)
        {
            return true;
        }

        return visibility switch
        {
            VisibilityLevel.Internal => isDataReader,
            VisibilityLevel.AllUsers => true,
            VisibilityLevel.Public => true,
            _ => false
        };
    }

    private async Task<bool> IssueAndSendContextEmailVerificationAsync(
        ContactAssignment assignment,
        Guid requestingUserId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(assignment.ContextEmail) || string.IsNullOrWhiteSpace(assignment.ContextEmailNormalized))
        {
            return false;
        }

        var oldTokens = await _dbContext.ContactAssignmentEmailVerificationTokens
            .Where(t => t.ContactAssignmentId == assignment.Id && !t.IsUsed)
            .ToListAsync(cancellationToken);
        foreach (var oldToken in oldTokens)
        {
            oldToken.IsUsed = true;
            oldToken.UsedAt = DateTime.UtcNow;
        }

        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');

        var tokenEntity = new ContactAssignmentEmailVerificationToken
        {
            Id = Guid.NewGuid(),
            ContactAssignmentId = assignment.Id,
            TokenHash = EmailAddressHelper.Sha256(rawToken),
            EmailNormalizedSnapshot = assignment.ContextEmailNormalized,
            ExpiresAt = DateTime.UtcNow.AddHours(72),
            IsUsed = false,
            CreatedAt = DateTime.UtcNow
        };
        await _dbContext.ContactAssignmentEmailVerificationTokens.AddAsync(tokenEntity, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Under /api so the link also reaches the backend behind the Vite dev proxy (only /api is forwarded there);
        // the route without /api stays mapped for links that were sent earlier.
        var verifyUrl = $"{_appSettings.BaseUrl.TrimEnd('/')}/api/contact-assignment-emails/verify?token={Uri.EscapeDataString(rawToken)}";
        var sent = await _emailService.SendContactAssignmentEmailVerificationAsync(
            assignment.ContextEmail,
            verifyUrl,
            cancellationToken);

        await _auditLogRepository.LogAsync(
            "ContactAssignment",
            assignment.Id,
            AuditAction.Update,
            requestingUserId,
            null,
            $"Context email verification token issued. Sent={sent}",
            cancellationToken);

        return sent;
    }

    /// <inheritdoc />
    public async Task<UserDataExport> ExportUserDataAsync(Guid requestingUserId, Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Check permissions: user can export their own data, or admin/manager can export anyone's
            var requestingUser = await _userRepository.GetByIdAsync(requestingUserId, cancellationToken);
            if (requestingUser == null)
            {
                throw new UnauthorizedAccessException("User not found.");
            }

            var isAdminOrManager = requestingUser.Claims.Contains(UserClaim.SystemAdmin) ||
                                   requestingUser.Claims.Contains(UserClaim.Manager);

            if (requestingUserId != userId && !isAdminOrManager)
            {
                throw new UnauthorizedAccessException("You can only export your own data.");
            }

            var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
            if (user == null)
            {
                throw new InvalidOperationException("User not found.");
            }

            var export = new UserDataExport
            {
                Metadata = new ExportMetadata
                {
                    ExportedAt = DateTime.UtcNow,
                    ExportVersion = "1.0",
                    RequestedBy = requestingUser.Email
                },
                Personal = new PersonalData
                {
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    Email = user.Email,
                    PhoneNumber = user.PhoneNumber
                },
                Account = new AccountData
                {
                    UserId = user.Id,
                    EmailVerified = user.EmailVerified,
                    CreatedAt = user.CreatedAt,
                    UpdatedAt = user.UpdatedAt,
                    LastLoginAt = user.LastLoginAt,
                    Claims = user.Claims.Select(c => c.ToString()).ToList()
                },
                Privacy = new PrivacySettings
                {
                    EmailVisibility = user.EmailVisibility.ToString(),
                    PhoneVisibility = user.PhoneVisibility.ToString()
                },
                Assignments = new List<AssignmentData>(),
                AuditLog = new List<AuditLogEntry>()
            };

            var cardDavToken = await _dbContext.CardDavTokens
                .AsNoTracking()
                .Where(t => t.UserId == userId && !t.IsRevoked)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (cardDavToken != null || user.CardDavIncludeOrganizations)
            {
                export.CardDav = new CardDavAccessData
                {
                    IncludeOrganizations = user.CardDavIncludeOrganizations,
                    AccessCreatedAt = cardDavToken?.CreatedAt,
                    LastUsedAt = cardDavToken?.LastUsedAt
                };
            }

            if (user.HasAvatar && _avatarService != null)
            {
                var avatar = await _avatarService.GetUserAvatarAsync(userId, cancellationToken);
                if (avatar != null)
                {
                    export.Media.Add(new MediaFileData
                    {
                        Type = ProfilePictureMediaType,
                        Path = "media/profilbild.webp",
                        ContentType = "image/webp",
                        SizeBytes = avatar.Length
                    });
                }
            }

            // Get all club assignments (with loaded ContactAssignments)
            var clubs = await _clubRepository.GetAllWithDetailsAsync(cancellationToken);
            foreach (var club in clubs)
            {
                var assignment = club.ContactAssignments?.FirstOrDefault(ca => ca.UserId == userId);
                if (assignment != null)
                {
                    export.Assignments.Add(new AssignmentData
                    {
                        Type = "Club",
                        EntityId = club.Id,
                        EntityName = club.Name,
                        PositionName = assignment.ContactPosition?.Name,
                        AssignedAt = assignment.AssignedAt,
                        ContextEmail = assignment.ContextEmail,
                        ContextEmailVerified = assignment.ContextEmailVerified,
                        ContextEmailVerifiedAt = assignment.ContextEmailVerifiedAt,
                        ContextEmailVisibility = assignment.ContextEmailVisibility.ToString()
                    });
                }
            }

            // Get all department assignments (with loaded ContactAssignments)
            var departments = await _departmentRepository.GetAllWithDetailsAsync(cancellationToken);
            foreach (var dept in departments)
            {
                var assignment = dept.ContactAssignments?.FirstOrDefault(ca => ca.UserId == userId);
                if (assignment != null)
                {
                    export.Assignments.Add(new AssignmentData
                    {
                        Type = "Department",
                        EntityId = dept.Id,
                        EntityName = dept.Name,
                        PositionName = assignment.ContactPosition?.Name,
                        AssignedAt = assignment.AssignedAt,
                        ContextEmail = assignment.ContextEmail,
                        ContextEmailVerified = assignment.ContextEmailVerified,
                        ContextEmailVerifiedAt = assignment.ContextEmailVerifiedAt,
                        ContextEmailVisibility = assignment.ContextEmailVisibility.ToString()
                    });
                }
            }

            // Audit logs are SystemAdmin-only data: include them only for the data subject themselves
            // or a SystemAdmin, not for a Manager exporting someone else's data.
            if (requestingUserId == userId || requestingUser.Claims.Contains(UserClaim.SystemAdmin))
            {
                var auditLogs = await _auditLogRepository.GetByUserAsync(userId, cancellationToken);
                export.AuditLog = auditLogs.Select(log => new AuditLogEntry
                {
                    Timestamp = log.Timestamp,
                    Action = log.Action.ToString(),
                    EntityType = log.EntityType,
                    EntityId = log.EntityId,
                    OldValues = SanitizeAuditValuesForUserExport(log.OldValues),
                    NewValues = SanitizeAuditValuesForUserExport(log.NewValues)
                }).ToList();
            }

            _logger.LogInformation("User {RequestingUserId} exported data for user {UserId}", requestingUserId, userId);
            return export;
        }
        catch (Exception ex) when (ex is not UnauthorizedAccessException && ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Error exporting data for user {UserId}", userId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<byte[]> ExportUserDataArchiveAsync(Guid requestingUserId, Guid userId, CancellationToken cancellationToken = default)
    {
        var export = await ExportUserDataAsync(requestingUserId, userId, cancellationToken);

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var json = JsonSerializer.Serialize(export, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            await WriteZipEntryAsync(archive, "datenexport.json", Encoding.UTF8.GetBytes(json), cancellationToken);

            if (_avatarService != null)
            {
                foreach (var media in export.Media.Where(m => m.Type == ProfilePictureMediaType))
                {
                    var data = await _avatarService.GetUserAvatarAsync(userId, cancellationToken);
                    if (data != null)
                    {
                        await WriteZipEntryAsync(archive, media.Path, data, cancellationToken);
                    }
                }
            }
        }

        return buffer.ToArray();
    }

    private static async Task WriteZipEntryAsync(ZipArchive archive, string path, byte[] data, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        await using var entryStream = entry.Open();
        await entryStream.WriteAsync(data, cancellationToken);
    }

    private static string? SanitizeAuditValuesForUserExport(string? rawValues)
    {
        if (string.IsNullOrWhiteSpace(rawValues))
        {
            return rawValues;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(rawValues);
            if (parsed == null || parsed.Count == 0)
            {
                return rawValues;
            }

            var recipientsKey = parsed.Keys.FirstOrDefault(k => string.Equals(k, "Recipients", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(recipientsKey))
            {
                return rawValues;
            }

            var recipients = parsed[recipientsKey];
            if (recipients.ValueKind != JsonValueKind.Array)
            {
                return rawValues;
            }

            var payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(rawValues);
            if (payload == null)
            {
                return rawValues;
            }

            payload[recipientsKey] = BuildRecipientNamesOnly(recipients);
            return JsonSerializer.Serialize(payload);
        }
        catch (JsonException)
        {
            return rawValues;
        }
    }

    private static List<Dictionary<string, string>> BuildRecipientNamesOnly(JsonElement recipients)
    {
        var result = new List<Dictionary<string, string>>();
        var position = 1;

        foreach (var recipient in recipients.EnumerateArray())
        {
            var fallback = $"Empfänger {position}";
            string? name = null;

            if (recipient.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in recipient.EnumerateObject())
                {
                    if (string.Equals(property.Name, "Name", StringComparison.OrdinalIgnoreCase) &&
                        property.Value.ValueKind == JsonValueKind.String)
                    {
                        name = property.Value.GetString();
                        break;
                    }
                }
            }
            else if (recipient.ValueKind == JsonValueKind.String)
            {
                name = recipient.GetString();
            }

            result.Add(new Dictionary<string, string>
            {
                ["Name"] = string.IsNullOrWhiteSpace(name) ? fallback : name
            });

            position++;
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<DeleteAccountResponse> DeleteAccountAsync(Guid requestingUserId, Guid userId, string confirmEmail, string? reason = null, CancellationToken cancellationToken = default)
    {
        try
        {
            // Check permissions
            var requestingUser = await _userRepository.GetByIdAsync(requestingUserId, cancellationToken);
            if (requestingUser == null)
            {
                return new DeleteAccountResponse
                {
                    Success = false,
                    Message = "Requesting user not found."
                };
            }

            var isAdmin = requestingUser.Claims.Contains(UserClaim.SystemAdmin);
            var isManager = requestingUser.Claims.Contains(UserClaim.Manager);

            // Users can delete their own account, admins/managers can delete anyone
            if (requestingUserId != userId && !isAdmin && !isManager)
            {
                return new DeleteAccountResponse
                {
                    Success = false,
                    Message = "Du kannst nur dein eigenes Konto löschen."
                };
            }

            var userToDelete = await _userRepository.GetByIdAsync(userId, cancellationToken);
            if (userToDelete == null)
            {
                return new DeleteAccountResponse
                {
                    Success = false,
                    Message = "Benutzer nicht gefunden."
                };
            }

            // Verify email confirmation
            if (!userToDelete.Email.Equals(confirmEmail, StringComparison.OrdinalIgnoreCase))
            {
                return new DeleteAccountResponse
                {
                    Success = false,
                    Message = "Die E-Mail-Bestätigung stimmt nicht überein."
                };
            }

            // Prevent deletion of the configured system admin user
            if (userToDelete.Email.Equals(_appSettings.SystemAdminEmail, StringComparison.OrdinalIgnoreCase))
            {
                return new DeleteAccountResponse
                {
                    Success = false,
                    Message = "Das konfigurierte System-Administrator-Konto kann nicht gelöscht werden."
                };
            }

            // Only a SystemAdmin may delete another SystemAdmin
            if (requestingUserId != userId && userToDelete.Claims.Contains(UserClaim.SystemAdmin) && !isAdmin)
            {
                _logger.LogWarning("User {UserId} attempted to delete SystemAdmin {TargetId} without SystemAdmin rights", requestingUserId, userId);
                return new DeleteAccountResponse
                {
                    Success = false,
                    Message = "Nur System-Administratoren können System-Administrator-Konten löschen."
                };
            }

            // Prevent deleting the last system admin
            if (userToDelete.Claims.Contains(UserClaim.SystemAdmin))
            {
                var allAdmins = (await _userRepository.GetAllAsync(cancellationToken))
                    .Where(u => u.Claims.Contains(UserClaim.SystemAdmin));

                if (allAdmins.Count() <= 1)
                {
                    return new DeleteAccountResponse
                    {
                        Success = false,
                        Message = "Das letzte System-Administrator-Konto kann nicht gelöscht werden."
                    };
                }
            }

            // Count assignments that will be cascade-deleted
            var userWithAssignments = await _userRepository.GetByIdWithAssignmentsAsync(userId, cancellationToken);
            int affectedAssignments = userWithAssignments?.ContactAssignments?.Count ?? 0;

            // Log the deletion before actually deleting
            await _auditLogRepository.LogAsync(new AuditLog
            {
                UserId = requestingUserId,
                Action = AuditAction.Delete,
                EntityType = "User",
                EntityId = userId,
                Timestamp = DateTime.UtcNow,
                OldValues = $"Email: {userToDelete.Email}, Name: {userToDelete.FirstName} {userToDelete.LastName}",
                NewValues = $"Deletion reason: {reason ?? "Not specified"}, Deleted by: {requestingUser.Email}"
            }, cancellationToken);

            // Delete all related tokens and data for the user
            var deletedInvitations = await _invitationTokenRepository.DeleteByEmailAsync(userToDelete.Email, cancellationToken);
            _logger.LogInformation("Deleted {Count} invitation tokens for user {Email}", deletedInvitations, EmailAddressHelper.Mask(userToDelete.Email));

            // Delete user (cascade deletes contact assignments)
            await _userRepository.DeleteAsync(userId, cancellationToken);
            var avatarCleanedUp = await CleanupUserAvatarAsync(userId, cancellationToken);

            _logger.LogInformation("User {RequestingUserId} deleted account {UserId}. Reason: {Reason}. Affected assignments: {Count}",
                requestingUserId, userId, LogSanitizer.Clean(reason ?? "Not specified"), affectedAssignments);

            return new DeleteAccountResponse
            {
                Success = true,
                Message = avatarCleanedUp
                    ? "Konto erfolgreich gelöscht."
                    : "Konto erfolgreich gelöscht. Das Profilbild konnte nicht vollständig entfernt werden und wird vom Administrator bereinigt.",
                DeletedAt = DateTime.UtcNow,
                AffectedAssignments = affectedAssignments,
                MediaCleanupFailed = !avatarCleanedUp
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting account {UserId}", userId);
            return new DeleteAccountResponse
            {
                Success = false,
                Message = "Ein Fehler ist beim Löschen des Kontos aufgetreten."
            };
        }
    }

    /// <returns>False if the avatar files could not be removed and remain as orphans in storage.</returns>
    private async Task<bool> CleanupUserAvatarAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (_avatarService == null)
        {
            return true;
        }

        try
        {
            await _avatarService.DeleteUserAvatarAsync(userId, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "User {UserId} was deleted, but avatar files in avatars/users/{UserId} could not be removed and must be cleaned up manually (GDPR erasure incomplete)",
                userId, userId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<InvitationResponse> InviteUserAsync(Guid requestingUserId, InviteUserDto dto, CancellationToken cancellationToken = default)
    {
        var requestingUser = await _userRepository.GetByIdAsync(requestingUserId, cancellationToken);
        if (requestingUser == null)
            throw new UnauthorizedAccessException("User not found.");

        // Validate mandatory application settings are configured before allowing invitations
        var requiredSettings = await _appSettingsService.GetValuesAsync(
            ["PrivacyPolicyUrl", "ContactName", "ContactEmail"], cancellationToken);

        var missingSettings = requiredSettings
            .Where(kv => string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => kv.Key)
            .ToList();

        if (missingSettings.Count > 0)
        {
            _logger.LogWarning("Invitation blocked: mandatory settings not configured: {MissingSettings}",
                string.Join(", ", missingSettings));
            return new InvitationResponse
            {
                Success = false,
                Message = "Einladungen können erst versendet werden, wenn alle Pflichteinstellungen (Datenschutz-URL, Kontaktname, Kontakt-E-Mail) in den Einstellungen konfiguriert sind."
            };
        }

        // Validate ClubId XOR DepartmentId
        if (dto.ClubId.HasValue && dto.DepartmentId.HasValue)
        {
            return new InvitationResponse
            {
                Success = false,
                Message = "Eine Einladung kann entweder einem Verein oder einer Abteilung bzw. Gruppe zugeordnet werden, nicht beiden."
            };
        }

        // Validate referenced entities exist
        if (dto.ClubId.HasValue)
        {
            var club = await _clubRepository.GetByIdAsync(dto.ClubId.Value, cancellationToken);
            if (club == null)
                return new InvitationResponse { Success = false, Message = "Der angegebene Verein wurde nicht gefunden." };
        }

        if (dto.DepartmentId.HasValue)
        {
            var department = await _departmentRepository.GetByIdAsync(dto.DepartmentId.Value, cancellationToken);
            if (department == null)
                return new InvitationResponse { Success = false, Message = "Die angegebene Abteilung oder Gruppe wurde nicht gefunden." };
        }

        if (dto.ContactPositionId.HasValue)
        {
            var position = await _contactPositionRepository.GetByIdAsync(dto.ContactPositionId.Value, cancellationToken);
            if (position == null)
                return new InvitationResponse { Success = false, Message = "Die angegebene Position wurde nicht gefunden." };
        }

        // The inviter must be allowed to edit the club / department the invitee is assigned to.
        // Invitations without an assignment are reserved for SystemAdmin and Manager.
        if (!await CanInviteIntoScopeAsync(requestingUserId, dto.ClubId, dto.DepartmentId, cancellationToken))
        {
            _logger.LogWarning(
                "User {UserId} attempted to invite into club {ClubId} / department {DepartmentId} without edit rights",
                requestingUserId, dto.ClubId, dto.DepartmentId);
            return new InvitationResponse
            {
                Success = false,
                Message = "Du kannst nur in Vereine, Abteilungen oder Gruppen einladen, die du selbst bearbeiten darfst."
            };
        }

        // Validate that the inviter is allowed to grant the requested claims
        if (dto.PendingClaims != null && dto.PendingClaims.Count > 0)
        {
            var inviterIsSystemAdmin = requestingUser.Claims?.Contains(UserClaim.SystemAdmin) == true;
            var inviterIsManager = requestingUser.Claims?.Contains(UserClaim.Manager) == true;
            var unauthorizedClaims = dto.PendingClaims
                .Where(claim =>
                {
                    // SystemAdmin can grant any claim
                    if (inviterIsSystemAdmin) return false;
                    // Manager can grant any claim except SystemAdmin
                    if (inviterIsManager && claim != UserClaim.SystemAdmin) return false;
                    return true;
                })
                .Distinct()
                .ToList();

            if (unauthorizedClaims.Count > 0)
            {
                _logger.LogWarning(
                    "User {UserId} attempted to grant claims they do not hold: {Claims}",
                    requestingUserId, LogSanitizer.Clean(string.Join(", ", unauthorizedClaims)));
                return new InvitationResponse
                {
                    Success = false,
                    Message = "Du kannst nur Berechtigungen vergeben, die du selbst besitzt."
                };
            }
        }

        // Check if the user already exists
        var existingUser = await _userRepository.GetByEmailAsync(dto.Email, cancellationToken);
        if (existingUser != null)
        {
            _logger.LogInformation("User {Email} already exists, informing inviter", EmailAddressHelper.Mask(dto.Email));
            return new InvitationResponse
            {
                Success = false,
                Message = "Ein Benutzer mit dieser E-Mail-Adresse existiert bereits.",
                IsExistingUser = true
            };
        }

        // Optional role/office address for the assignment; the login stays on the personal address
        var contextEmail = string.IsNullOrWhiteSpace(dto.ContextEmail) ? null : dto.ContextEmail.Trim();
        if (contextEmail != null)
        {
            if (!dto.ClubId.HasValue && !dto.DepartmentId.HasValue)
            {
                return new InvitationResponse
                {
                    Success = false,
                    Message = "Eine Amtsadresse kann nur zusammen mit einer Zuordnung zu einem Verein, einer Abteilung oder einer Gruppe angegeben werden."
                };
            }

            var contextEmailError = await ValidateInvitationContextEmailAsync(contextEmail, dto.Email, cancellationToken);
            if (contextEmailError != null)
                return new InvitationResponse { Success = false, Message = contextEmailError };
        }

        // Generate a secure token
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(tokenBytes)
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');

        var invitation = new InvitationToken
        {
            Id = Guid.NewGuid(),
            Email = dto.Email,
            Token = token,
            InvitedById = requestingUserId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(_appSettings.InvitationTokenExpirationDays),
            ClubId = dto.ClubId,
            DepartmentId = dto.DepartmentId,
            ContactPositionId = dto.ContactPositionId,
            CanEdit = dto.CanEdit,
            AssignmentDetails = string.IsNullOrWhiteSpace(dto.AssignmentDetails) ? null : dto.AssignmentDetails.Trim(),
            ContextEmail = contextEmail,
            PendingClaims = dto.PendingClaims?.Distinct().ToList() ?? new List<UserClaim>()
        };

        await _invitationTokenRepository.AddAsync(invitation, cancellationToken);

        // Build URLs - these will be relative paths that the frontend handles
        var onboardingUrl = $"/onboarding?token={Uri.EscapeDataString(token)}";
        var revokeUrl = $"/api/invitations/revoke?token={Uri.EscapeDataString(token)}";

        await _emailService.SendInvitationEmailAsync(
            dto.Email,
            $"{requestingUser.FirstName} {requestingUser.LastName}",
            onboardingUrl,
            revokeUrl,
            _appSettings.InvitationTokenExpirationDays,
            cancellationToken);

        await _auditLogRepository.LogAsync(new AuditLog
        {
            UserId = requestingUserId,
            Action = AuditAction.Create,
            EntityType = "InvitationToken",
            EntityId = invitation.Id,
            Timestamp = DateTime.UtcNow,
            NewValues = $"Invited: {dto.Email}, ClubId: {dto.ClubId}, DepartmentId: {dto.DepartmentId}, PositionId: {dto.ContactPositionId}"
        }, cancellationToken);

        _logger.LogInformation("User {RequestingUserId} invited {Email} (ClubId={ClubId}, DepartmentId={DepartmentId})",
            requestingUserId, EmailAddressHelper.Mask(dto.Email), dto.ClubId, dto.DepartmentId);

        return new InvitationResponse
        {
            Success = true,
            Message = "Einladung wurde erfolgreich versendet."
        };
    }

    /// <inheritdoc />
    public async Task<AcceptInvitationResponse> AcceptInvitationAsync(AcceptInvitationDto dto, CancellationToken cancellationToken = default)
    {
        var invitation = await _invitationTokenRepository.GetByTokenAsync(dto.Token, cancellationToken);
        if (invitation == null || invitation.IsUsed || invitation.IsRevoked || invitation.ExpiresAt < DateTime.UtcNow)
        {
            return new AcceptInvitationResponse
            {
                Success = false,
                Message = "Die Einladung ist ungültig oder abgelaufen."
            };
        }

        if (!dto.AcceptedPrivacyPolicy)
        {
            return new AcceptInvitationResponse
            {
                Success = false,
                Message = "Die Datenschutzerklärung muss akzeptiert werden."
            };
        }

        // An account may have been created for this mailbox since the invitation was sent
        if (await _userRepository.GetByEmailAsync(invitation.Email, cancellationToken) != null)
        {
            return new AcceptInvitationResponse
            {
                Success = false,
                Message = "Für diese E-Mail-Adresse existiert bereits ein Konto. Bitte melde dich an."
            };
        }

        // Get ALL valid invitations for this email to consolidate pre-linked assignments
        var allInvitations = await _invitationTokenRepository.GetAllValidByEmailAsync(invitation.Email, cancellationToken);

        // Role/office addresses per assignment: the invitee may confirm, change or remove the inviter's suggestion
        var contextEmails = new Dictionary<Guid, string>();
        foreach (var inv in allInvitations.Where(inv => inv.ClubId.HasValue || inv.DepartmentId.HasValue))
        {
            var requested = dto.AssignmentContextEmails != null && dto.AssignmentContextEmails.TryGetValue(inv.Id, out var overridden)
                ? overridden
                : inv.ContextEmail;
            if (string.IsNullOrWhiteSpace(requested))
                continue;

            var contextEmailError = await ValidateInvitationContextEmailAsync(requested.Trim(), invitation.Email, cancellationToken);
            if (contextEmailError != null)
                return new AcceptInvitationResponse { Success = false, Message = contextEmailError };

            contextEmails[inv.Id] = requested.Trim();
        }

        // Create the user
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = invitation.Email,
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            PhoneNumber = dto.PhoneNumber,
            EmailVisibility = dto.EmailVisibility,
            PhoneVisibility = dto.PhoneVisibility,
            EmailVerified = true,
            PreferredLoginMethod = dto.PreferredLoginMethod,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedById = invitation.InvitedById
        };

        await _userRepository.AddAsync(user, cancellationToken);

        // Consolidate and apply only inviter-authorized pending claims from all invitations
        var pendingClaims = new HashSet<UserClaim>();
        foreach (var inv in allInvitations.Where(inv => inv.PendingClaims.Count > 0))
        {
            var inviter = await _userRepository.GetByIdAsync(inv.InvitedById, cancellationToken);
            if (inviter == null)
            {
                _logger.LogWarning(
                    "Skipping pending claims from invitation {InvitationId} for email {Email} because inviter {InviterId} could not be loaded.",
                    inv.Id,
                    inv.Email,
                    inv.InvitedById);
                continue;
            }

            var inviterIsSystemAdmin = inviter.Claims?.Contains(UserClaim.SystemAdmin) == true;
            var inviterIsManager = inviter.Claims?.Contains(UserClaim.Manager) == true;
            var unauthorizedClaims = inv.PendingClaims
                .Where(claim =>
                {
                    if (inviterIsSystemAdmin) return false;
                    if (inviterIsManager && claim != UserClaim.SystemAdmin) return false;
                    return true;
                })
                .Distinct()
                .ToList();

            if (unauthorizedClaims.Count > 0)
            {
                _logger.LogWarning(
                    "Skipping unauthorized pending claims {Claims} from invitation {InvitationId} for email {Email}. Inviter {InviterId} is not permitted to grant them.",
                    string.Join(", ", unauthorizedClaims),
                    inv.Id,
                    inv.Email,
                    inv.InvitedById);
            }

            foreach (var claim in inv.PendingClaims.Where(claim =>
                inviterIsSystemAdmin || (inviterIsManager && claim != UserClaim.SystemAdmin)))
            {
                pendingClaims.Add(claim);
            }
        }

        if (pendingClaims.Count > 0)
        {
            user.Claims = pendingClaims.ToList();
            await _userRepository.UpdateAsync(user, cancellationToken);
            _logger.LogInformation("Applied pending claims {Claims} to user {UserId}", string.Join(", ", user.Claims), user.Id);
        }

        // Create contact assignments from all invitations with pre-linking
        var createdAssignments = 0;
        var assignmentsToVerify = new List<ContactAssignment>();
        foreach (var inv in allInvitations)
        {
            // Re-check that the inviter still may edit the scope, so lost rights are not passed on
            if ((inv.ClubId.HasValue || inv.DepartmentId.HasValue) &&
                !await CanInviteIntoScopeAsync(inv.InvitedById, inv.ClubId, inv.DepartmentId, cancellationToken))
            {
                _logger.LogWarning(
                    "Skipping assignment from invitation {InvitationId} because inviter {InviterId} can no longer edit club {ClubId} / department {DepartmentId}",
                    inv.Id, inv.InvitedById, inv.ClubId, inv.DepartmentId);
            }
            else if (inv.ClubId.HasValue || inv.DepartmentId.HasValue)
            {
                var assignment = new ContactAssignment
                {
                    UserId = user.Id,
                    ClubId = inv.ClubId,
                    DepartmentId = inv.DepartmentId,
                    ContactPositionId = inv.ContactPositionId,
                    CanEdit = inv.CanEdit,
                    Notes = inv.AssignmentDetails,
                    AssignedAt = DateTime.UtcNow
                };

                if (contextEmails.TryGetValue(inv.Id, out var contextEmail))
                {
                    assignment.ContextEmail = contextEmail;
                    assignment.ContextEmailNormalized = EmailAddressHelper.Normalize(contextEmail);
                    assignment.ContextEmailVerified = false;
                }

                if (inv.ClubId.HasValue)
                {
                    var club = await _clubRepository.GetByIdAsync(inv.ClubId.Value, cancellationToken);
                    if (club != null)
                    {
                        var existingAssignment = club.ContactAssignments
                            .FirstOrDefault(ca => ca.UserId == user.Id && ca.ClubId == inv.ClubId);

                        if (existingAssignment == null)
                        {
                            club.ContactAssignments.Add(assignment);
                            await _clubRepository.UpdateAsync(club, cancellationToken);
                            createdAssignments++;
                            if (assignment.ContextEmail != null)
                                assignmentsToVerify.Add(assignment);
                        }
                        else if (inv.CanEdit && !existingAssignment.CanEdit)
                        {
                            existingAssignment.CanEdit = true;
                            await _clubRepository.UpdateAsync(club, cancellationToken);
                        }
                    }
                }
                else if (inv.DepartmentId.HasValue)
                {
                    var department = await _departmentRepository.GetByIdAsync(inv.DepartmentId.Value, cancellationToken);
                    if (department != null)
                    {
                        var existingAssignment = department.ContactAssignments
                            .FirstOrDefault(ca => ca.UserId == user.Id && ca.DepartmentId == inv.DepartmentId);

                        if (existingAssignment == null)
                        {
                            department.ContactAssignments.Add(assignment);
                            await _departmentRepository.UpdateAsync(department, cancellationToken);
                            createdAssignments++;
                            if (assignment.ContextEmail != null)
                                assignmentsToVerify.Add(assignment);
                        }
                        else if (inv.CanEdit && !existingAssignment.CanEdit)
                        {
                            existingAssignment.CanEdit = true;
                            await _departmentRepository.UpdateAsync(department, cancellationToken);
                        }
                    }
                }
            }

            // Mark invitation as used
            inv.IsUsed = true;
            inv.UsedAt = DateTime.UtcNow;
            await _invitationTokenRepository.UpdateAsync(inv, cancellationToken);
        }

        // Role/office addresses are only used after the mailbox owner confirmed them
        foreach (var assignment in assignmentsToVerify)
        {
            try
            {
                await IssueAndSendContextEmailVerificationAsync(assignment, user.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                // The account exists already; the user can resend the confirmation under "Mein Konto"
                _logger.LogWarning(ex, "Failed to send context email verification for assignment {AssignmentId}", assignment.Id);
            }
        }

        // Audit log
        await _auditLogRepository.LogAsync(new AuditLog
        {
            UserId = user.Id,
            Action = AuditAction.Create,
            EntityType = "User",
            EntityId = user.Id,
            Timestamp = DateTime.UtcNow,
            NewValues = $"User created via invitation, {createdAssignments} contact assignments created from {allInvitations.Count} invitations"
        }, cancellationToken);

        _logger.LogInformation("User {Email} accepted invitation and created account {UserId} with {AssignmentCount} contact assignments",
            invitation.Email, user.Id, createdAssignments);

        return new AcceptInvitationResponse
        {
            Success = true,
            Message = "Konto erfolgreich erstellt. Du kannst dich jetzt anmelden.",
            LoginUrl = "/login",
            Email = invitation.Email
        };
    }

    /// <summary>
    /// An inviter may assign invitees only to clubs / departments they can edit
    /// (CanEdit contact, parent-club editor, Manager or SystemAdmin).
    /// Invitations without an assignment require Manager or SystemAdmin.
    /// </summary>
    private async Task<bool> CanInviteIntoScopeAsync(Guid inviterId, Guid? clubId, Guid? departmentId, CancellationToken cancellationToken)
    {
        if (clubId.HasValue)
            return await _authorizationService.CanEditClubAsync(inviterId, clubId.Value, cancellationToken);

        if (departmentId.HasValue)
            return await _authorizationService.CanEditDepartmentAsync(inviterId, departmentId.Value, cancellationToken);

        var inviter = await _userRepository.GetByIdAsync(inviterId, cancellationToken);
        return inviter?.Claims?.Contains(UserClaim.SystemAdmin) == true ||
               inviter?.Claims?.Contains(UserClaim.Manager) == true;
    }

    /// <summary>
    /// Checks a role/office address (context email) proposed during invitation or registration.
    /// Returns a user-facing error message, or null if the address can be used.
    /// </summary>
    private async Task<string?> ValidateInvitationContextEmailAsync(string contextEmail, string loginEmail, CancellationToken cancellationToken)
    {
        if (contextEmail.Length > 255 || !MailAddress.TryCreate(contextEmail, out _))
            return "Die Amtsadresse ist keine gültige E-Mail-Adresse.";

        var normalized = EmailAddressHelper.Normalize(contextEmail);
        if (string.Equals(normalized, EmailAddressHelper.Normalize(loginEmail), StringComparison.OrdinalIgnoreCase))
            return "Die Amtsadresse muss sich von der persönlichen E-Mail-Adresse unterscheiden.";

        if (await _dbContext.Users.AsNoTracking().AnyAsync(u => u.Email == normalized, cancellationToken))
            return "Diese Adresse ist bereits die persönliche Anmeldeadresse eines Benutzerkontos und kann nicht als Amtsadresse genutzt werden.";

        return null;
    }

    /// <inheritdoc />
    public async Task<bool> RevokeInvitationAsync(string token, CancellationToken cancellationToken = default)
    {
        var invitation = await _invitationTokenRepository.GetByTokenAsync(token, cancellationToken);
        if (invitation == null || invitation.IsUsed || invitation.IsRevoked)
            return false;

        invitation.IsRevoked = true;
        invitation.RevokedAt = DateTime.UtcNow;
        await _invitationTokenRepository.UpdateAsync(invitation, cancellationToken);

        _logger.LogInformation("Invitation for {Email} was revoked", EmailAddressHelper.Mask(invitation.Email));
        return true;
    }

    /// <inheritdoc />
    public async Task<InvitationDetailsResponse?> GetInvitationDetailsAsync(string token, CancellationToken cancellationToken = default)
    {
        var invitation = await _invitationTokenRepository.GetByTokenAsync(token, cancellationToken);
        if (invitation == null || invitation.IsUsed || invitation.IsRevoked || invitation.ExpiresAt < DateTime.UtcNow)
            return null;

        // Get all valid invitations for this email to show pending assignments
        var allInvitations = await _invitationTokenRepository.GetAllValidByEmailAsync(invitation.Email, cancellationToken);

        var pendingAssignments = allInvitations
            .Where(i => i.ClubId.HasValue || i.DepartmentId.HasValue)
            .Select(i => new PendingAssignmentDto
            {
                InvitationId = i.Id,
                ContextEmail = i.ContextEmail,
                ClubName = i.Club?.Name ?? i.Department?.Club?.Name,
                DepartmentName = i.Department?.Name,
                ParentDepartmentName = i.Department?.ParentDepartment?.Name,
                PositionName = i.ContactPosition?.Name
            })
            .ToList();

        var privacyPolicyUrl = await _appSettingsService.GetValueAsync("PrivacyPolicyUrl", cancellationToken)
            ?? "";

        return new InvitationDetailsResponse
        {
            Email = invitation.Email,
            ExpiresAt = invitation.ExpiresAt,
            PrivacyPolicyUrl = privacyPolicyUrl,
            PendingAssignments = pendingAssignments,
            PendingClaims = allInvitations.SelectMany(i => i.PendingClaims).Distinct().ToList()
        };
    }

}
