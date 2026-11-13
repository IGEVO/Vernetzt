using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json;
using Igevo.Vernetzt.Core.Configuration;
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

/// <summary>
/// Changes the personal (login) address of an account. The new address only becomes active once the
/// link sent to it is opened, so a typo cannot lock anyone out. Passkeys, sessions, assignments and
/// feed tokens are bound to the user id and keep working.
/// </summary>
public class AccountEmailChangeService : IAccountEmailChangeService
{
    internal const int TokenLifetimeHours = 72;
    internal const int MaxRequestsPerHour = 5;

    private readonly VernetztDbContext _dbContext;
    private readonly IEmailService _emailService;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly AppSettings _appSettings;
    private readonly ILogger<AccountEmailChangeService> _logger;

    public AccountEmailChangeService(
        VernetztDbContext dbContext,
        IEmailService emailService,
        IAuditLogRepository auditLogRepository,
        IOptions<AppSettings> appSettings,
        ILogger<AccountEmailChangeService> logger)
    {
        _dbContext = dbContext;
        _emailService = emailService;
        _auditLogRepository = auditLogRepository;
        _appSettings = appSettings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AccountEmailChangeRequestResult> RequestChangeAsync(
        Guid requestingUserId,
        Guid userId,
        string newEmail,
        CancellationToken cancellationToken = default)
    {
        var requestingUser = await _dbContext.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == requestingUserId, cancellationToken);
        if (requestingUser == null)
        {
            return Result(AccountEmailChangeStatus.Forbidden, "Keine Berechtigung.");
        }

        var user = await _dbContext.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user == null)
        {
            return Result(AccountEmailChangeStatus.NotFound, "Benutzer nicht gefunden.");
        }

        if (!MayChange(requestingUser, user))
        {
            _logger.LogWarning("User {RequestingUserId} attempted to change the login address of {UserId} without permission", requestingUserId, userId);
            return Result(AccountEmailChangeStatus.Forbidden, "Keine Berechtigung.");
        }

        if (IsBootstrapAdminAddress(user.Email))
        {
            return Result(AccountEmailChangeStatus.Invalid,
                "Die Adresse dieses Kontos ist in der Server-Konfiguration als System-Administration hinterlegt (App:SystemAdminEmail) und kann nur dort geändert werden.");
        }

        var normalized = EmailAddressHelper.Normalize(newEmail);
        if (normalized == null || !new EmailAddressAttribute().IsValid(normalized) || normalized.Length > 255)
        {
            return Result(AccountEmailChangeStatus.Invalid, "Ungültige E-Mail-Adresse.");
        }

        if (normalized == EmailAddressHelper.Normalize(user.Email))
        {
            return Result(AccountEmailChangeStatus.Invalid, "Das ist bereits die aktuelle Adresse.");
        }

        var conflict = await FindConflictAsync(userId, normalized, cancellationToken);
        if (conflict != null)
        {
            return Result(AccountEmailChangeStatus.Conflict, conflict);
        }

        var now = DateTime.UtcNow;
        var recentRequests = await _dbContext.AccountEmailChangeTokens
            .CountAsync(t => t.UserId == userId && t.CreatedAt > now.AddHours(-1), cancellationToken);
        if (recentRequests >= MaxRequestsPerHour)
        {
            return Result(AccountEmailChangeStatus.Conflict, "Zu viele Anfragen. Bitte versuche es in einer Stunde erneut.");
        }

        // Only the latest request counts
        var openTokens = await _dbContext.AccountEmailChangeTokens
            .Where(t => t.UserId == userId && !t.IsUsed)
            .ToListAsync(cancellationToken);
        foreach (var openToken in openTokens)
        {
            openToken.IsUsed = true;
            openToken.UsedAt = now;
        }

        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');

        _dbContext.AccountEmailChangeTokens.Add(new AccountEmailChangeToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = EmailAddressHelper.Sha256(rawToken),
            NewEmail = normalized,
            OldEmailSnapshot = user.Email,
            RequestedByUserId = requestingUserId,
            ExpiresAt = now.AddHours(TokenLifetimeHours),
            CreatedAt = now
        });
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Under /api so the link also reaches the backend behind the Vite dev proxy
        var confirmUrl = $"{_appSettings.BaseUrl.TrimEnd('/')}/api/account-email-change/confirm?token={Uri.EscapeDataString(rawToken)}";
        var sent = await _emailService.SendAccountEmailChangeConfirmationAsync(normalized, user.FullName, confirmUrl, cancellationToken);

        await _auditLogRepository.LogAsync(
            entityType: nameof(User),
            entityId: userId,
            action: AuditAction.Update,
            userId: requestingUserId,
            oldValues: null,
            newValues: JsonSerializer.Serialize(new
            {
                Action = "AccountEmailChangeRequested",
                NewEmailMasked = EmailAddressHelper.Mask(normalized),
                Sent = sent
            }),
            cancellationToken: cancellationToken);

        if (!sent)
        {
            return Result(AccountEmailChangeStatus.SendFailed, "Die Bestätigungs-E-Mail konnte nicht versendet werden. Bitte versuche es später erneut.");
        }

        _logger.LogInformation("Login address change requested for user {UserId} by {RequestingUserId}", userId, requestingUserId);
        return Result(AccountEmailChangeStatus.ConfirmationSent,
            $"Wir haben einen Bestätigungslink an {normalized} geschickt. Die neue Adresse gilt erst, wenn der Link geöffnet wurde (gültig {TokenLifetimeHours} Stunden).");
    }

    /// <inheritdoc />
    public async Task<AccountEmailChangeConfirmationResultDto> ConfirmChangeAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Failure("Ungültiger Link", "Der Bestätigungslink ist ungültig.");
        }

        var tokenHash = EmailAddressHelper.Sha256(token.Trim());
        var changeToken = await _dbContext.AccountEmailChangeTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        if (changeToken == null)
        {
            return Failure("Link nicht gefunden", "Der Bestätigungslink wurde nicht gefunden oder ist nicht mehr gültig.");
        }

        if (changeToken.IsUsed)
        {
            return Failure("Link nicht mehr gültig",
                "Dieser Bestätigungslink wurde bereits verwendet oder durch eine neuere Anfrage ersetzt.");
        }

        if (changeToken.ExpiresAt < DateTime.UtcNow)
        {
            return Failure("Link abgelaufen",
                "Dieser Bestätigungslink ist abgelaufen. Unter „Mein Konto“ kannst du die Änderung erneut anfordern.");
        }

        var user = changeToken.User;
        if (!string.Equals(user.Email, changeToken.OldEmailSnapshot, StringComparison.OrdinalIgnoreCase))
        {
            return Failure("Adresse bereits geändert",
                "Die Adresse des Kontos wurde inzwischen geändert. Bitte fordere die Änderung erneut an.");
        }

        if (IsBootstrapAdminAddress(user.Email))
        {
            return Failure("Änderung nicht möglich",
                "Die Adresse dieses Kontos ist in der Server-Konfiguration hinterlegt und kann nur dort geändert werden.");
        }

        // The address may have been taken since the request
        var conflict = await FindConflictAsync(user.Id, changeToken.NewEmail, cancellationToken);
        if (conflict != null)
        {
            return Failure("Änderung nicht möglich", conflict);
        }

        var now = DateTime.UtcNow;
        var oldEmail = user.Email;

        user.Email = changeToken.NewEmail;   // mirrored to the Identity account on save
        user.EmailVerified = true;
        changeToken.IsUsed = true;
        changeToken.UsedAt = now;

        // Failure notices of open relay tokens should reach the current address
        var openRelayTokens = await _dbContext.EmailRelayTokens
            .Where(t => t.CreatedByUserId == user.Id && !t.IsUsed && t.ExpiresAt > now)
            .ToListAsync(cancellationToken);
        foreach (var relayToken in openRelayTokens)
        {
            relayToken.SenderEmail = changeToken.NewEmail;
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // Unique index on Users.Email: another account took the address in the meantime
            _logger.LogWarning(ex, "Login address change for user {UserId} failed to save", user.Id);
            return Failure("Änderung nicht möglich", "Diese Adresse wird bereits von einem anderen Konto verwendet.");
        }

        await _auditLogRepository.LogAsync(
            entityType: nameof(User),
            entityId: user.Id,
            action: AuditAction.Update,
            userId: changeToken.RequestedByUserId,
            oldValues: JsonSerializer.Serialize(new { EmailMasked = EmailAddressHelper.Mask(oldEmail) }),
            newValues: JsonSerializer.Serialize(new
            {
                Action = "AccountEmailChanged",
                EmailMasked = EmailAddressHelper.Mask(changeToken.NewEmail),
                UpdatedRelayTokens = openRelayTokens.Count
            }),
            cancellationToken: cancellationToken);

        _logger.LogInformation("Login address of user {UserId} changed", user.Id);

        // Tell the previous mailbox, so a change the owner did not want is noticed
        try
        {
            await _emailService.SendAccountEmailChangedNoticeAsync(
                oldEmail,
                user.FullName,
                EmailAddressHelper.Mask(changeToken.NewEmail),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to notify previous address of user {UserId} about the address change", user.Id);
        }

        return new AccountEmailChangeConfirmationResultDto
        {
            Success = true,
            Title = "Adresse geändert",
            Message = $"Die Anmeldung bei Vernetzt erfolgt ab jetzt mit {changeToken.NewEmail}. Passkeys und bestehende Anmeldungen bleiben gültig."
        };
    }

    private static bool MayChange(User requestingUser, User user)
    {
        if (requestingUser.Id == user.Id || requestingUser.Claims.Contains(UserClaim.SystemAdmin))
        {
            return true;
        }

        // Managers may not touch SystemAdmin accounts (same rule as for profile updates)
        return requestingUser.Claims.Contains(UserClaim.Manager) && !user.Claims.Contains(UserClaim.SystemAdmin);
    }

    /// <summary>
    /// The configured bootstrap admin is looked up by address on every start: renaming it would create a second
    /// admin, and giving its address to another account would grant that account SystemAdmin rights.
    /// </summary>
    private bool IsBootstrapAdminAddress(string? email)
    {
        var adminEmail = EmailAddressHelper.Normalize(_appSettings.SystemAdminEmail);
        return adminEmail != null && adminEmail == EmailAddressHelper.Normalize(email);
    }

    private async Task<string?> FindConflictAsync(Guid userId, string normalizedEmail, CancellationToken cancellationToken)
    {
        if (IsBootstrapAdminAddress(normalizedEmail))
        {
            return "Diese Adresse ist für die System-Administration reserviert.";
        }

        if (await _dbContext.Users.AnyAsync(u => u.Id != userId && u.Email == normalizedEmail, cancellationToken))
        {
            return "Diese Adresse wird bereits von einem anderen Konto verwendet.";
        }

        var now = DateTime.UtcNow;
        if (await _dbContext.InvitationTokens.AnyAsync(i => i.Email.ToLower() == normalizedEmail && !i.IsUsed && !i.IsRevoked && i.ExpiresAt > now, cancellationToken))
        {
            return "Für diese Adresse ist eine Einladung offen. Bitte die Einladung zuerst zurückziehen.";
        }

        if (await _dbContext.ContactAssignments.AnyAsync(a => a.UserId != userId && a.ContextEmailNormalized == normalizedEmail, cancellationToken))
        {
            return "Diese Adresse ist bei einer anderen Person als Amtsadresse hinterlegt.";
        }

        return null;
    }

    private static AccountEmailChangeRequestResult Result(AccountEmailChangeStatus status, string message) => new(status, message);

    private static AccountEmailChangeConfirmationResultDto Failure(string title, string message) => new()
    {
        Success = false,
        Title = title,
        Message = message
    };
}
