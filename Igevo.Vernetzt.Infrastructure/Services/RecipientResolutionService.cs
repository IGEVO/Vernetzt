using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Igevo.Vernetzt.Infrastructure.Services;

public class RecipientResolutionService : IRecipientResolutionService
{
    private readonly VernetztDbContext _dbContext;
    private readonly IAuthorizationService _authorizationService;
    private readonly ILogger<RecipientResolutionService> _logger;

    public RecipientResolutionService(
        VernetztDbContext dbContext,
        IAuthorizationService authorizationService,
        ILogger<RecipientResolutionService> logger)
    {
        _dbContext = dbContext;
        _authorizationService = authorizationService;
        _logger = logger;
    }

    public async Task<RecipientResolutionResult> ResolveForSendAsync(
        Guid requestingUserId,
        IEnumerable<Guid> recipientAssignmentIds,
        IEnumerable<Guid> recipientUserIds,
        CancellationToken cancellationToken = default)
    {
        var assignmentIds = recipientAssignmentIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        var userIds = recipientUserIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        var recipientsByEmail = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var requestedCount = assignmentIds.Count + userIds.Count;

        if (assignmentIds.Count > 0)
        {
            var assignmentResolution = await ResolveFromAssignmentsAsync(requestingUserId, assignmentIds, cancellationToken);
            if (assignmentResolution.Forbidden)
            {
                assignmentResolution.RequestedRecipientCount = requestedCount;
                return assignmentResolution;
            }

            MergeRecipientResults(recipientsByEmail, assignmentResolution);
        }

        if (userIds.Count > 0)
        {
            var userResolution = await ResolveFromUsersAsync(userIds, cancellationToken);
            MergeRecipientResults(recipientsByEmail, userResolution);
        }

        return new RecipientResolutionResult
        {
            Success = recipientsByEmail.Count > 0,
            RecipientEmails = recipientsByEmail.Keys.ToList(),
            RecipientDisplayItems = recipientsByEmail.Values.ToList(),
            RequestedRecipientCount = requestedCount
        };
    }

    public async Task<List<string>> ResolveDisplayItemsByEmailAsync(
        IEnumerable<string> recipientEmails,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmails = recipientEmails
            .Select(EmailAddressHelper.Normalize)
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedEmails.Count == 0)
        {
            return [];
        }

        var assignmentCandidates = await _dbContext.ContactAssignments
            .AsNoTracking()
            .Include(a => a.User)
            .Include(a => a.Club)
            .Include(a => a.Department)
                .ThenInclude(d => d!.Club)
            .Where(a => a.ConfirmedAt != null && (
                (a.ContextEmailVerified
                 && a.ContextEmailNormalized != null
                 && normalizedEmails.Contains(a.ContextEmailNormalized))
                || (a.User.Email != null
                    && normalizedEmails.Contains(a.User.Email.ToLower()))))
            .ToListAsync(cancellationToken);

        var users = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Email != null && normalizedEmails.Contains(u.Email.ToLower()))
            .ToListAsync(cancellationToken);

        var usersByEmail = users
            .Select(u => new { Email = EmailAddressHelper.Normalize(u.Email), User = u })
            .Where(x => !string.IsNullOrWhiteSpace(x.Email))
            .GroupBy(x => x.Email!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().User, StringComparer.OrdinalIgnoreCase);

        var displayItems = new List<string>(normalizedEmails.Count);
        foreach (var email in normalizedEmails)
        {
            var assignment = assignmentCandidates
                .Where(a => string.Equals(GetEffectiveEmail(a), email, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(a => a.ContextEmailVerified && !string.IsNullOrWhiteSpace(a.ContextEmailNormalized))
                .ThenBy(a => a.Department?.Name ?? string.Empty)
                .ThenBy(a => a.Club?.Name ?? a.Department?.Club?.Name ?? string.Empty)
                .FirstOrDefault();

            if (assignment != null)
            {
                displayItems.Add(BuildAssignmentDisplayItem(assignment));
                continue;
            }

            if (usersByEmail.TryGetValue(email, out var user))
            {
                displayItems.Add(BuildUserDisplayName(user));
                continue;
            }

            displayItems.Add("Unbekannter Kontakt");
        }

        return displayItems;
    }

    private async Task<RecipientResolutionResult> ResolveFromAssignmentsAsync(
        Guid requestingUserId,
        List<Guid> assignmentIds,
        CancellationToken cancellationToken)
    {
        var assignments = await _dbContext.ContactAssignments
            .AsNoTracking()
            .Include(a => a.User)
            .Include(a => a.Club)
            .Include(a => a.Department)
                .ThenInclude(d => d!.Club)
            // Pending assignments (no consent yet) cannot be addressed and count as missing
            .Where(a => assignmentIds.Contains(a.Id) && a.ConfirmedAt != null)
            .ToListAsync(cancellationToken);

        var foundIds = assignments.Select(a => a.Id).ToHashSet();
        var missingIds = assignmentIds.Where(id => !foundIds.Contains(id)).ToList();

        var forbiddenIds = new List<Guid>();
        foreach (var assignment in assignments)
        {
            var accessible = assignment.ClubId.HasValue
                ? await _authorizationService.CanAccessClubAsync(requestingUserId, assignment.ClubId.Value, cancellationToken)
                : assignment.DepartmentId.HasValue
                    ? await _authorizationService.CanAccessDepartmentAsync(requestingUserId, assignment.DepartmentId.Value, cancellationToken)
                    : false;

            if (!accessible)
            {
                forbiddenIds.Add(assignment.Id);
            }
        }

        if (missingIds.Count > 0 || forbiddenIds.Count > 0)
        {
            var allForbidden = missingIds.Concat(forbiddenIds).Distinct().ToList();
            _logger.LogWarning(
                "Recipient resolution denied for user {UserId}. Requested={Requested}, Missing={Missing}, Forbidden={Forbidden}",
                requestingUserId,
                assignmentIds.Count,
                missingIds.Count,
                forbiddenIds.Count);

            return new RecipientResolutionResult
            {
                Success = false,
                Forbidden = true,
                ForbiddenAssignmentIds = allForbidden,
                RequestedRecipientCount = assignmentIds.Count
            };
        }

        var assignmentsById = assignments.ToDictionary(a => a.Id);
        var recipientsByEmail = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var assignmentId in assignmentIds)
        {
            if (!assignmentsById.TryGetValue(assignmentId, out var assignment))
            {
                continue;
            }

            var effectiveEmail = GetEffectiveEmail(assignment);

            if (!string.IsNullOrWhiteSpace(effectiveEmail) && !recipientsByEmail.ContainsKey(effectiveEmail))
            {
                recipientsByEmail[effectiveEmail] = BuildAssignmentDisplayItem(assignment);
            }
        }

        return new RecipientResolutionResult
        {
            Success = recipientsByEmail.Count > 0,
            RecipientEmails = recipientsByEmail.Keys.ToList(),
            RecipientDisplayItems = recipientsByEmail.Values.ToList(),
            RequestedRecipientCount = assignmentIds.Count
        };
    }

    private async Task<RecipientResolutionResult> ResolveFromUsersAsync(
        IEnumerable<Guid> recipientUserIds,
        CancellationToken cancellationToken)
    {
        var userIds = recipientUserIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        var users = await _dbContext.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToListAsync(cancellationToken);

        var usersById = users.ToDictionary(u => u.Id);
        var recipientsByEmail = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var userId in userIds)
        {
            if (!usersById.TryGetValue(userId, out var user))
            {
                continue;
            }

            var normalized = EmailAddressHelper.Normalize(user.Email);
            if (!string.IsNullOrWhiteSpace(normalized) && !recipientsByEmail.ContainsKey(normalized))
            {
                recipientsByEmail[normalized] = BuildUserDisplayName(user);
            }
        }

        return new RecipientResolutionResult
        {
            Success = recipientsByEmail.Count > 0,
            RecipientEmails = recipientsByEmail.Keys.ToList(),
            RecipientDisplayItems = recipientsByEmail.Values.ToList(),
            RequestedRecipientCount = userIds.Count
        };
    }

    private static string GetEffectiveEmail(ContactAssignment assignment)
    {
        if (assignment.ContextEmailVerified && !string.IsNullOrWhiteSpace(assignment.ContextEmailNormalized))
        {
            return assignment.ContextEmailNormalized;
        }

        return EmailAddressHelper.Normalize(assignment.User.Email) ?? string.Empty;
    }

    private static string BuildUserDisplayName(User user)
    {
        var fullName = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrWhiteSpace(fullName) ? "Unbekannter Kontakt" : fullName;
    }

    private static string BuildAssignmentDisplayItem(ContactAssignment assignment)
    {
        var name = BuildUserDisplayName(assignment.User);
        var context = BuildAssignmentContextLabel(assignment);

        return string.IsNullOrWhiteSpace(context) ? name : $"{name} ({context})";
    }

    private static string BuildAssignmentContextLabel(ContactAssignment assignment)
    {
        if (assignment.Department != null)
        {
            var departmentName = assignment.Department.Name;
            var clubName = assignment.Department.Club?.Name ?? assignment.Club?.Name;

            if (!string.IsNullOrWhiteSpace(departmentName) && !string.IsNullOrWhiteSpace(clubName))
            {
                return $"{departmentName}, {clubName}";
            }

            if (!string.IsNullOrWhiteSpace(departmentName))
            {
                return departmentName;
            }
        }

        if (assignment.Club != null && !string.IsNullOrWhiteSpace(assignment.Club.Name))
        {
            return assignment.Club.Name;
        }

        return string.Empty;
    }

    private static void MergeRecipientResults(
        Dictionary<string, string> recipientsByEmail,
        RecipientResolutionResult resolution)
    {
        for (var i = 0; i < resolution.RecipientEmails.Count; i++)
        {
            var email = resolution.RecipientEmails[i];
            if (string.IsNullOrWhiteSpace(email) || recipientsByEmail.ContainsKey(email))
            {
                continue;
            }

            var displayItem = i < resolution.RecipientDisplayItems.Count
                ? resolution.RecipientDisplayItems[i]
                : "Unbekannter Kontakt";
            recipientsByEmail[email] = string.IsNullOrWhiteSpace(displayItem)
                ? "Unbekannter Kontakt"
                : displayItem;
        }
    }
}
