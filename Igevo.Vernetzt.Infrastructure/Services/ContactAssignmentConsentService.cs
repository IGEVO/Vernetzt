using Igevo.Vernetzt.Core.DTOs.Users;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Igevo.Vernetzt.Infrastructure.Services;

/// <inheritdoc />
public class ContactAssignmentConsentService(
    VernetztDbContext dbContext,
    IAuthorizationService authorizationService,
    IEmailService emailService,
    IAuditLogRepository auditLogRepository,
    ILogger<ContactAssignmentConsentService> logger) : IContactAssignmentConsentService
{
    /// <summary>Minimum time between two consent reminders for the same assignment.</summary>
    public static readonly TimeSpan ReminderCooldown = TimeSpan.FromHours(24);

    private const string AuditEntityType = nameof(ContactAssignment);

    public async Task<ContactAssignmentConsentResult> ConfirmAsync(
        Guid userId, Guid assignmentId, VisibilityLevel visibility, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(visibility))
            return new(ContactAssignmentConsentStatus.Conflict, "Ungültige Sichtbarkeit.");

        var assignment = await dbContext.ContactAssignments
            .FirstOrDefaultAsync(a => a.Id == assignmentId && a.UserId == userId, cancellationToken);
        if (assignment is null)
            return new(ContactAssignmentConsentStatus.NotFound, "Zuordnung nicht gefunden.");
        if (assignment.IsConfirmed)
            return new(ContactAssignmentConsentStatus.Conflict, "Dieser Zuordnung hast du bereits zugestimmt.");

        assignment.ConfirmedAt = DateTime.UtcNow;
        assignment.Visibility = visibility;
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditLogRepository.LogAsync(
            AuditEntityType, assignment.Id, AuditAction.Update, userId,
            "Consent pending",
            $"Consent given. Visibility={visibility}",
            cancellationToken);
        logger.LogInformation("User {UserId} confirmed assignment {AssignmentId} with visibility {Visibility}", userId, assignmentId, visibility);

        return new(ContactAssignmentConsentStatus.Success, "Danke! Die Zuordnung ist jetzt aktiv.");
    }

    public async Task<ContactAssignmentConsentResult> DeclineAsync(
        Guid userId, Guid assignmentId, CancellationToken cancellationToken = default)
    {
        var assignment = await LoadWithOrganizationAsync(assignmentId, cancellationToken);
        if (assignment is null || assignment.UserId != userId)
            return new(ContactAssignmentConsentStatus.NotFound, "Zuordnung nicht gefunden.");
        if (assignment.IsConfirmed)
            return new(ContactAssignmentConsentStatus.Conflict,
                "Einer bestätigten Zuordnung kannst du nicht mehr widersprechen. Wende dich an die Personen, die die Organisation bearbeiten, oder stelle die Sichtbarkeit auf „Nur Administration & Verwaltung“.");

        var organizationName = OrganizationName(assignment);
        var recipients = await GetDeclineRecipientsAsync(assignment, cancellationToken);
        var declinedBy = assignment.User.FullName;

        dbContext.ContactAssignments.Remove(assignment);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditLogRepository.LogAsync(
            AuditEntityType, assignment.Id, AuditAction.Delete, userId,
            $"Pending assignment to {organizationName}",
            "Declined by assigned user",
            cancellationToken);
        logger.LogInformation("User {UserId} declined assignment {AssignmentId}", userId, assignmentId);

        foreach (var recipient in recipients)
        {
            try
            {
                await emailService.SendAssignmentDeclinedNotificationAsync(
                    recipient.Email, recipient.FullName, declinedBy, organizationName, cancellationToken);
            }
            catch (Exception ex)
            {
                // The assignment is gone either way; the notification is best effort
                logger.LogWarning(ex, "Failed to send decline notification for assignment {AssignmentId}", assignmentId);
            }
        }

        return new(ContactAssignmentConsentStatus.Success, "Die Zuordnung wurde abgelehnt und entfernt.");
    }

    public async Task<ContactAssignmentConsentResult> SendReminderAsync(
        Guid requestingUserId, Guid assignmentId, CancellationToken cancellationToken = default)
    {
        var assignment = await LoadWithOrganizationAsync(assignmentId, cancellationToken);
        if (assignment is null)
            return new(ContactAssignmentConsentStatus.NotFound, "Zuordnung nicht gefunden.");

        var canEdit = assignment.ClubId is { } clubId
            ? await authorizationService.CanEditClubAsync(requestingUserId, clubId, cancellationToken)
            : assignment.DepartmentId is { } departmentId
              && await authorizationService.CanEditDepartmentAsync(requestingUserId, departmentId, cancellationToken);
        if (!canEdit)
            return new(ContactAssignmentConsentStatus.Forbidden, "Keine Berechtigung.");
        if (assignment.IsConfirmed)
            return new(ContactAssignmentConsentStatus.Conflict, "Die Person hat der Zuordnung bereits zugestimmt.");

        var now = DateTime.UtcNow;
        if (assignment.LastConsentRequestAt is { } last && now - last < ReminderCooldown)
            return new(ContactAssignmentConsentStatus.TooManyRequests,
                "Die Person wurde in den letzten 24 Stunden bereits erinnert. Bitte versuche es später erneut.");

        var sent = await SendConsentRequestAsync(assignment, cancellationToken);
        if (!sent)
            return new(ContactAssignmentConsentStatus.Conflict, "Die Erinnerung konnte nicht versendet werden.");

        assignment.LastConsentRequestAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditLogRepository.LogAsync(
            AuditEntityType, assignment.Id, AuditAction.Update, requestingUserId,
            null, "Consent reminder sent", cancellationToken);

        return new(ContactAssignmentConsentStatus.Success, $"Erinnerung an {assignment.User.FullName} wurde versendet.");
    }

    private async Task<bool> SendConsentRequestAsync(ContactAssignment assignment, CancellationToken cancellationToken)
    {
        if (assignment.Club is { } club)
        {
            return await emailService.SendClubAssignmentConsentRequestAsync(
                assignment.User.Email, assignment.User.FullName, club.Name, assignment.Id, assignment.CanEdit, cancellationToken);
        }

        if (assignment.Department is { } department)
        {
            return await emailService.SendDepartmentAssignmentConsentRequestAsync(
                assignment.User.Email, assignment.User.FullName, department.Name, department.Club?.Name ?? "Unbekannter Verein",
                assignment.Id, assignment.CanEdit, department.ParentDepartment?.Name, cancellationToken);
        }

        return false;
    }

    private Task<ContactAssignment?> LoadWithOrganizationAsync(Guid assignmentId, CancellationToken cancellationToken) =>
        dbContext.ContactAssignments
            .Include(a => a.User)
            .Include(a => a.Club)
            .Include(a => a.Department).ThenInclude(d => d!.Club)
            .Include(a => a.Department).ThenInclude(d => d!.ParentDepartment)
            .FirstOrDefaultAsync(a => a.Id == assignmentId, cancellationToken);

    private static string OrganizationName(ContactAssignment assignment) => assignment.Club is { } club
        ? club.Name
        : assignment.Department is { } department
            ? string.Join(" › ", new[] { department.Club?.Name, department.ParentDepartment?.Name, department.Name }
                .Where(n => !string.IsNullOrWhiteSpace(n)))
            : "Unbekannte Organisation";

    /// <summary>
    /// The person who created the assignment. Nobody if users assigned themselves; only if the creator is
    /// unknown (legacy data) or no longer exists, the confirmed editors of the entity are informed instead.
    /// </summary>
    private async Task<List<User>> GetDeclineRecipientsAsync(ContactAssignment assignment, CancellationToken cancellationToken)
    {
        if (assignment.AssignedById is { } assignedById)
        {
            // Self-assignment: declining informs nobody else
            if (assignedById == assignment.UserId) return [];

            var assignedBy = await dbContext.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == assignedById, cancellationToken);
            if (assignedBy is not null) return [assignedBy];
        }

        return await dbContext.ContactAssignments.AsNoTracking()
            .Where(a => a.Id != assignment.Id && a.CanEdit && a.ConfirmedAt != null && a.UserId != assignment.UserId
                        && (assignment.ClubId != null ? a.ClubId == assignment.ClubId : a.DepartmentId == assignment.DepartmentId))
            .Select(a => a.User)
            .ToListAsync(cancellationToken);
    }
}
