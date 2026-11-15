using Igevo.Vernetzt.Core.DTOs.Admin;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Igevo.Vernetzt.Infrastructure.Services;

/// <inheritdoc />
public class PendingItemsService(
    VernetztDbContext dbContext,
    IAuditLogRepository auditLogRepository,
    ILogger<PendingItemsService> logger) : IPendingItemsService
{
    public async Task<PendingItemsOverviewDto> GetOverviewAsync(Guid requestingUserId, CancellationToken cancellationToken = default)
    {
        await EnsureManagerOrAdminAsync(requestingUserId, cancellationToken);

        var now = DateTime.UtcNow;

        var invitations = await dbContext.InvitationTokens
            .AsNoTracking()
            .Where(i => !i.IsUsed && !i.IsRevoked && i.ExpiresAt > now)
            .Select(i => new
            {
                Invitation = i,
                ClubName = i.Club != null ? i.Club.Name : i.Department != null ? i.Department.Club.Name : null,
                ClubId = i.ClubId ?? (i.Department != null ? i.Department.ClubId : (Guid?)null),
                DepartmentName = i.Department != null ? i.Department.Name : null,
                ParentDepartmentName = i.Department != null && i.Department.ParentDepartment != null ? i.Department.ParentDepartment.Name : null,
                PositionName = i.ContactPosition != null ? i.ContactPosition.Name : null,
                InvitedByName = dbContext.Users
                    .Where(u => u.Id == i.InvitedById)
                    .Select(u => u.FirstName + " " + u.LastName)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var assignments = await dbContext.ContactAssignments
            .AsNoTracking()
            .Where(a => a.ConfirmedAt == null)
            .Select(a => new
            {
                a.Id,
                a.UserId,
                UserName = a.User.FirstName + " " + a.User.LastName,
                UserEmail = a.User.Email,
                a.DepartmentId,
                ClubId = a.ClubId ?? (a.Department != null ? a.Department.ClubId : (Guid?)null),
                ClubName = a.Club != null ? a.Club.Name : a.Department != null ? a.Department.Club.Name : null,
                DepartmentName = a.Department != null ? a.Department.Name : null,
                ParentDepartmentName = a.Department != null && a.Department.ParentDepartment != null ? a.Department.ParentDepartment.Name : null,
                PositionName = a.ContactPosition != null ? a.ContactPosition.Name : null,
                AssignedByName = dbContext.Users
                    .Where(u => u.Id == a.AssignedById)
                    .Select(u => u.FirstName + " " + u.LastName)
                    .FirstOrDefault(),
                a.AssignedAt,
                a.LastConsentRequestAt
            })
            .ToListAsync(cancellationToken);

        var contextEmails = await dbContext.ContactAssignments
            .AsNoTracking()
            .Where(a => a.ContextEmail != null && a.ContextEmail != "" && !a.ContextEmailVerified)
            .Select(a => new
            {
                a.Id,
                a.UserId,
                UserName = a.User.FirstName + " " + a.User.LastName,
                ContextEmail = a.ContextEmail!,
                a.DepartmentId,
                ClubId = a.ClubId ?? (a.Department != null ? a.Department.ClubId : (Guid?)null),
                ClubName = a.Club != null ? a.Club.Name : a.Department != null ? a.Department.Club.Name : null,
                DepartmentName = a.Department != null ? a.Department.Name : null,
                ParentDepartmentName = a.Department != null && a.Department.ParentDepartment != null ? a.Department.ParentDepartment.Name : null,
                PositionName = a.ContactPosition != null ? a.ContactPosition.Name : null,
                a.ConfirmedAt,
                LastVerificationSentAt = dbContext.ContactAssignmentEmailVerificationTokens
                    .Where(t => t.ContactAssignmentId == a.Id)
                    .Max(t => (DateTime?)t.CreatedAt)
            })
            .ToListAsync(cancellationToken);

        return new PendingItemsOverviewDto
        {
            // Oldest first: those are the ones most likely to need a nudge.
            Invitations = invitations
                .OrderBy(x => x.Invitation.CreatedAt)
                .Select(x => new PendingInvitationDto
                {
                    Id = x.Invitation.Id,
                    Email = x.Invitation.Email,
                    InvitedByName = x.InvitedByName,
                    CreatedAt = x.Invitation.CreatedAt,
                    ExpiresAt = x.Invitation.ExpiresAt,
                    ContextEmail = x.Invitation.ContextEmail,
                    PendingClaims = x.Invitation.PendingClaims,
                    ClubId = x.ClubId,
                    DepartmentId = x.Invitation.DepartmentId,
                    ClubName = x.ClubName,
                    ParentDepartmentName = x.ParentDepartmentName,
                    DepartmentName = x.DepartmentName,
                    PositionName = x.PositionName
                })
                .ToList(),
            Assignments = assignments
                .OrderBy(x => x.AssignedAt)
                .Select(x => new PendingAssignmentConsentDto
                {
                    AssignmentId = x.Id,
                    UserId = x.UserId,
                    UserName = x.UserName,
                    UserEmail = x.UserEmail,
                    AssignedByName = x.AssignedByName,
                    AssignedAt = x.AssignedAt,
                    LastConsentRequestAt = x.LastConsentRequestAt,
                    ClubId = x.ClubId,
                    DepartmentId = x.DepartmentId,
                    ClubName = x.ClubName,
                    ParentDepartmentName = x.ParentDepartmentName,
                    DepartmentName = x.DepartmentName,
                    PositionName = x.PositionName
                })
                .ToList(),
            ContextEmails = contextEmails
                .OrderBy(x => x.LastVerificationSentAt ?? DateTime.MinValue)
                .Select(x => new PendingContextEmailDto
                {
                    AssignmentId = x.Id,
                    UserId = x.UserId,
                    UserName = x.UserName,
                    ContextEmail = x.ContextEmail,
                    LastVerificationSentAt = x.LastVerificationSentAt,
                    IsAssignmentConfirmed = x.ConfirmedAt != null,
                    ClubId = x.ClubId,
                    DepartmentId = x.DepartmentId,
                    ClubName = x.ClubName,
                    ParentDepartmentName = x.ParentDepartmentName,
                    DepartmentName = x.DepartmentName,
                    PositionName = x.PositionName
                })
                .ToList()
        };
    }

    public async Task<bool> RevokeInvitationAsync(Guid requestingUserId, Guid invitationId, CancellationToken cancellationToken = default)
    {
        await EnsureManagerOrAdminAsync(requestingUserId, cancellationToken);

        var invitation = await dbContext.InvitationTokens
            .FirstOrDefaultAsync(i => i.Id == invitationId, cancellationToken);
        if (invitation is null || invitation.IsUsed || invitation.IsRevoked || invitation.ExpiresAt <= DateTime.UtcNow)
            return false;

        invitation.IsRevoked = true;
        invitation.RevokedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditLogRepository.LogAsync(
            nameof(InvitationToken), invitation.Id, AuditAction.Update, requestingUserId,
            null, "Invitation revoked by administration", cancellationToken);
        logger.LogInformation("User {UserId} revoked invitation {InvitationId}", requestingUserId, invitation.Id);
        return true;
    }

    private async Task EnsureManagerOrAdminAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null || !(user.Claims.Contains(UserClaim.SystemAdmin) || user.Claims.Contains(UserClaim.Manager)))
            throw new UnauthorizedAccessException("Only system administrators and managers can view pending items.");
    }
}
