using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Igevo.Vernetzt.Infrastructure.Services;

/// <summary>
/// Service for managing clubs with authorization checks.
/// </summary>
public class ClubService : IClubService
{
    private readonly IClubRepository _clubRepository;
    private readonly IUserRepository _userRepository;
    private readonly IAuthorizationService _authorizationService;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IEmailService _emailService;
    private readonly IAvatarService _avatarService;
    private readonly VernetztDbContext _dbContext;
    private readonly ILogger<ClubService> _logger;

    public ClubService(
        IClubRepository clubRepository,
        IUserRepository userRepository,
        IAuthorizationService authorizationService,
        IAuditLogRepository auditLogRepository,
        IEmailService emailService,
        IAvatarService avatarService,
        VernetztDbContext dbContext,
        ILogger<ClubService> _logger)
    {
        _clubRepository = clubRepository;
        _userRepository = userRepository;
        _authorizationService = authorizationService;
        _auditLogRepository = auditLogRepository;
        _emailService = emailService;
        _avatarService = avatarService;
        _dbContext = dbContext;
        this._logger = _logger;
    }

    public async Task<IEnumerable<Club>> GetAccessibleClubsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var clubIds = await _authorizationService.GetAccessibleClubIdsAsync(userId, cancellationToken);
        var allClubs = await _clubRepository.GetAllWithDetailsAsync(cancellationToken);

        var accessibleClubs = allClubs.Where(c => clubIds.Contains(c.Id)).ToList();

        _logger.LogInformation(
            "User {UserId} retrieved {Count} accessible clubs",
            userId,
            accessibleClubs.Count);

        return accessibleClubs;
    }

    public async Task<Club?> GetByIdAsync(Guid userId, Guid clubId, CancellationToken cancellationToken = default)
    {
        if (!await _authorizationService.CanAccessClubAsync(userId, clubId, cancellationToken))
        {
            _logger.LogWarning("User {UserId} denied access to club {ClubId}", userId, clubId);
            return null;
        }

        var club = await _clubRepository.GetByIdAsync(clubId, cancellationToken);
        return club;
    }

    public async Task<Club> CreateAsync(Guid userId, Club club, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null || !(user.Claims.Contains(UserClaim.SystemAdmin) || user.Claims.Contains(UserClaim.Manager)))
        {
            _logger.LogWarning("User {UserId} attempted to create club without permission", userId);
            throw new UnauthorizedAccessException("Only system administrators and managers can create clubs");
        }

        club.Id = Guid.NewGuid();
        club.CreatedAt = DateTime.UtcNow;

        // Trim string inputs
        club.Name = club.Name?.Trim() ?? string.Empty;
        club.ShortName = TextNormalization.TrimToNull(club.ShortName);
        club.Description = TextNormalization.TrimToNull(club.Description);
        club.Street = club.Street?.Trim();
        club.PostalCode = club.PostalCode?.Trim();
        club.City = club.City?.Trim();
        club.Email = club.Email?.Trim();
        club.WebsiteUrl = club.WebsiteUrl?.Trim();
        club.SocialMediaLinks = SocialMediaLink.Normalize(club.SocialMediaLinks);

        var createdClub = await _clubRepository.AddAsync(club, cancellationToken);

        await _auditLogRepository.LogAsync(
            "Club",
            createdClub.Id,
            AuditAction.Create,
            userId,
            null,
            $"Created club: {createdClub.Name}",
            cancellationToken);

        _logger.LogInformation("User {UserId} created club {ClubId} ({ClubName})", userId, createdClub.Id, createdClub.Name);

        return createdClub;
    }

    public async Task<Club?> UpdateAsync(Guid userId, Guid clubId, Club club, CancellationToken cancellationToken = default)
    {
        if (!await _authorizationService.CanEditClubAsync(userId, clubId, cancellationToken))
        {
            _logger.LogWarning("User {UserId} denied edit access to club {ClubId}", userId, clubId);
            return null;
        }

        var existingClub = await _clubRepository.GetByIdAsync(clubId, cancellationToken);
        if (existingClub == null)
        {
            _logger.LogWarning("Club {ClubId} not found for update", clubId);
            return null;
        }

        var previousState = CreateClubAuditState(existingClub);
        var updatedState = CreateClubAuditState(club);

        // Update properties on the tracked entity to avoid EF Core tracking conflicts (trim string inputs)
        existingClub.Name = updatedState.Name;
        existingClub.ShortName = updatedState.ShortName;
        existingClub.Description = TextNormalization.TrimToNull(club.Description);
        existingClub.ClubTypeId = updatedState.ClubTypeId;
        existingClub.Street = updatedState.Street;
        existingClub.PostalCode = updatedState.PostalCode;
        existingClub.City = updatedState.City;
        existingClub.WebsiteUrl = updatedState.WebsiteUrl;
        existingClub.SocialMediaLinks = SocialMediaLink.Normalize(club.SocialMediaLinks);
        existingClub.Email = updatedState.Email;
        existingClub.ContactPersonsVisibility = updatedState.ContactPersonsVisibility;

        var updatedClub = await _clubRepository.UpdateAsync(existingClub, cancellationToken);
        var (oldValues, newValues) = BuildChangedClubAuditValues(previousState, updatedState);

        await _auditLogRepository.LogAsync(
            "Club",
            clubId,
            AuditAction.Update,
            userId,
            oldValues,
            newValues,
            cancellationToken);

        _logger.LogInformation("User {UserId} updated club {ClubId}", userId, clubId);

        return updatedClub;
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid clubId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null || !(user.Claims.Contains(UserClaim.SystemAdmin) || user.Claims.Contains(UserClaim.Manager)))
        {
            _logger.LogWarning("User {UserId} attempted to delete club without permission", userId);
            return false;
        }

        var club = await _clubRepository.GetByIdAsync(clubId, cancellationToken);
        if (club == null)
        {
            _logger.LogWarning("Club {ClubId} not found for deletion", clubId);
            return false;
        }

        var departmentIds = club.Departments.Select(d => d.Id).ToList();
        var deleted = await _clubRepository.DeleteAsync(clubId, cancellationToken);

        if (deleted)
        {
            await _auditLogRepository.LogAsync(
                "Club",
                clubId,
                AuditAction.Delete,
                userId,
                $"Deleted club: {club.Name}",
                null,
                cancellationToken);

            await CleanupAvatarsAfterClubDeletionAsync(clubId, departmentIds, cancellationToken);

            _logger.LogInformation("User {UserId} deleted club {ClubId} ({ClubName})", userId, clubId, club.Name);
        }

        return deleted;
    }

    private async Task CleanupAvatarsAfterClubDeletionAsync(
        Guid clubId,
        IEnumerable<Guid> departmentIds,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var departmentId in departmentIds)
            {
                await _avatarService.DeleteDepartmentAvatarAsync(departmentId, cancellationToken);
            }

            await _avatarService.DeleteClubAvatarAsync(clubId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Club {ClubId} was deleted, but avatar cleanup did not complete successfully",
                clubId);
        }
    }

    public async Task<bool> AddContactPersonAsync(
        Guid userId,
        Guid clubId,
        Guid contactPersonId,
        Guid? positionId = null,
        bool canEdit = false,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        if (!await _authorizationService.CanEditClubAsync(userId, clubId, cancellationToken))
        {
            _logger.LogWarning("User {UserId} denied permission to add contact person to club {ClubId}", userId, clubId);
            return false;
        }

        var club = await _clubRepository.GetByIdAsync(clubId, cancellationToken);
        if (club == null)
        {
            _logger.LogWarning("Club {ClubId} not found", clubId);
            return false;
        }

        var contactPerson = await _userRepository.GetByIdAsync(contactPersonId, cancellationToken);
        if (contactPerson == null)
        {
            _logger.LogWarning("Contact person {ContactPersonId} not found", contactPersonId);
            return false;
        }

        if (club.ContactAssignments.Any(ca => ca.UserId == contactPersonId))
        {
            _logger.LogWarning("User {ContactPersonId} already assigned to club {ClubId}", contactPersonId, clubId);
            return false;
        }

        club.ContactAssignments.Add(new ContactAssignment
        {
            UserId = contactPersonId,
            ClubId = clubId,
            ContactPositionId = positionId,
            CanEdit = canEdit,
            AssignedAt = DateTime.UtcNow,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        });
        club.UpdatedAt = DateTime.UtcNow;
        await _clubRepository.UpdateAsync(club, cancellationToken);

        await _auditLogRepository.LogAsync(
            "Club",
            clubId,
            AuditAction.Update,
            userId,
            null,
            $"Added contact person: {contactPerson.FullName}",
            cancellationToken);

        // Send email notification to contact person
        try
        {
            await _emailService.SendClubAssignmentNotificationAsync(
                contactPerson.Email,
                contactPerson.FullName,
                club.Name,
                clubId,
                canEdit,
                cancellationToken);
        }
        catch (Exception emailEx)
        {
            _logger.LogError(emailEx, "Failed to send club assignment notification email to {Email}", EmailAddressHelper.Mask(contactPerson.Email));
            // Don't fail the assignment if email fails
        }

        _logger.LogInformation(
            "User {UserId} added contact person {ContactPersonId} to club {ClubId}",
            userId,
            contactPersonId,
            clubId);

        return true;
    }

    public async Task<bool> UpdateContactPersonAsync(
        Guid userId,
        Guid clubId,
        Guid contactPersonId,
        Guid? positionId,
        bool canEdit = false,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        if (!await _authorizationService.CanEditClubAsync(userId, clubId, cancellationToken))
        {
            _logger.LogWarning("User {UserId} denied permission to update contact position in club {ClubId}", userId, clubId);
            return false;
        }

        var club = await _clubRepository.GetByIdAsync(clubId, cancellationToken);
        if (club == null)
        {
            _logger.LogWarning("Club {ClubId} not found", clubId);
            return false;
        }

        var assignment = club.ContactAssignments.FirstOrDefault(ca => ca.UserId == contactPersonId);
        if (assignment == null)
        {
            _logger.LogWarning("User {ContactPersonId} not assigned to club {ClubId}", contactPersonId, clubId);
            return false;
        }

        assignment.ContactPositionId = positionId;
        assignment.CanEdit = canEdit;
        assignment.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        club.UpdatedAt = DateTime.UtcNow;
        await _clubRepository.UpdateAsync(club, cancellationToken);

        await _auditLogRepository.LogAsync(
            "Club",
            clubId,
            AuditAction.Update,
            userId,
            null,
            $"Updated contact position for: {contactPersonId}",
            cancellationToken);

        _logger.LogInformation(
            "User {UserId} updated position for contact person {ContactPersonId} in club {ClubId}",
            userId,
            contactPersonId,
            clubId);

        return true;
    }

    public async Task<bool> RemoveContactPersonAsync(Guid userId, Guid clubId, Guid contactPersonId, CancellationToken cancellationToken = default)
    {
        if (!await _authorizationService.CanEditClubAsync(userId, clubId, cancellationToken))
        {
            _logger.LogWarning("User {UserId} denied permission to remove contact person from club {ClubId}", userId, clubId);
            return false;
        }

        var club = await _clubRepository.GetByIdAsync(clubId, cancellationToken);
        if (club == null)
        {
            _logger.LogWarning("Club {ClubId} not found", clubId);
            return false;
        }

        var assignment = club.ContactAssignments.FirstOrDefault(ca => ca.UserId == contactPersonId);
        if (assignment == null)
        {
            _logger.LogWarning("User {ContactPersonId} not assigned to club {ClubId}", contactPersonId, clubId);
            return false;
        }

        club.ContactAssignments.Remove(assignment);
        _dbContext.Remove(assignment);
        club.UpdatedAt = DateTime.UtcNow;
        await _clubRepository.UpdateAsync(club, cancellationToken);

        await _auditLogRepository.LogAsync(
            "Club",
            clubId,
            AuditAction.Update,
            userId,
            $"Removed contact person: {contactPersonId}",
            null,
            cancellationToken);

        _logger.LogInformation(
            "User {UserId} removed contact person {ContactPersonId} from club {ClubId}",
            userId,
            contactPersonId,
            clubId);

        return true;
    }

    private static ClubAuditState CreateClubAuditState(Club club) =>
        new(
            Name: NormalizeRequiredText(club.Name),
            ShortName: NormalizeOptionalText(club.ShortName),
            ClubTypeId: club.ClubTypeId,
            Street: NormalizeOptionalText(club.Street),
            PostalCode: NormalizeOptionalText(club.PostalCode),
            City: NormalizeOptionalText(club.City),
            WebsiteUrl: NormalizeOptionalText(club.WebsiteUrl),
            SocialMediaLinks: FormatSocialMediaLinks(club.SocialMediaLinks),
            Email: NormalizeOptionalText(club.Email),
            ContactPersonsVisibility: club.ContactPersonsVisibility);

    private static (string? OldValues, string? NewValues) BuildChangedClubAuditValues(
        ClubAuditState oldState,
        ClubAuditState newState)
    {
        var oldDictionary = oldState.ToAuditDictionary();
        var newDictionary = newState.ToAuditDictionary();

        var changedOldValues = new Dictionary<string, object?>();
        var changedNewValues = new Dictionary<string, object?>();

        foreach (var oldEntry in oldDictionary)
        {
            if (!newDictionary.TryGetValue(oldEntry.Key, out var newValue))
                continue;

            if (!Equals(oldEntry.Value, newValue))
            {
                changedOldValues[oldEntry.Key] = oldEntry.Value;
                changedNewValues[oldEntry.Key] = newValue;
            }
        }

        if (changedOldValues.Count == 0)
            return (null, null);

        return (
            JsonSerializer.Serialize(changedOldValues),
            JsonSerializer.Serialize(changedNewValues));
    }

    /// <summary>
    /// Flattens links into a comparable audit string ("Platform: URL; ..."), null when there are none.
    /// </summary>
    private static string? FormatSocialMediaLinks(IEnumerable<SocialMediaLink>? links)
    {
        var normalized = SocialMediaLink.Normalize(links);
        return normalized.Count == 0
            ? null
            : string.Join("; ", normalized.Select(l => $"{l.Platform}: {l.Url}"));
    }

    private static string NormalizeRequiredText(string? value) => value?.Trim() ?? string.Empty;

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private readonly record struct ClubAuditState(
        string Name,
        string? ShortName,
        Guid? ClubTypeId,
        string? Street,
        string? PostalCode,
        string? City,
        string? WebsiteUrl,
        string? SocialMediaLinks,
        string? Email,
        VisibilityLevel ContactPersonsVisibility)
    {
        public Dictionary<string, object?> ToAuditDictionary() =>
            new()
            {
                ["Name"] = Name,
                ["ShortName"] = ShortName,
                ["ClubTypeId"] = ClubTypeId?.ToString(),
                ["Street"] = Street,
                ["PostalCode"] = PostalCode,
                ["City"] = City,
                ["WebsiteUrl"] = WebsiteUrl,
                ["SocialMediaLinks"] = SocialMediaLinks,
                ["Email"] = Email,
                ["ContactPersonsVisibility"] = ContactPersonsVisibility.ToString()
            };
    }
}
