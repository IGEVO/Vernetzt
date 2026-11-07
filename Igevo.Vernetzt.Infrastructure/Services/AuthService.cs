using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Igevo.Vernetzt.Core.DTOs.Auth;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Data;
using Igevo.Vernetzt.Infrastructure.Identity;
using Igevo.Vernetzt.Infrastructure.Observability;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Igevo.Vernetzt.Infrastructure.Services;

/// <summary>
/// Login-link flow on top of ASP.NET Core Identity.
/// Token creation and validation are delegated to Identity's data-protection token provider
/// (registered as <see cref="LoginLinkTokens.ProviderName"/>); this service adds single use,
/// auditing and the context-email hint.
/// </summary>
public class AuthService : IAuthService
{
    private readonly UserManager<AppIdentityUser> _userManager;
    private readonly IUserRepository _userRepository;
    private readonly IEmailService _emailService;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly VernetztDbContext _dbContext;
    private readonly ILogger<AuthService> _logger;
    private readonly bool _contextEmailLoginHintEnabled;
    private readonly int _contextEmailLoginHintCooldownMinutes;
    private readonly int _loginLinkExpirationMinutes;
    private readonly int _loginLinkMaxPerUserPerHour;
    private readonly IMemoryCache _cache;
    private readonly VernetztMetrics? _metrics;

    public AuthService(
        UserManager<AppIdentityUser> userManager,
        IUserRepository userRepository,
        IEmailService emailService,
        IAuditLogRepository auditLogRepository,
        VernetztDbContext dbContext,
        IMemoryCache cache,
        IConfiguration configuration,
        ILogger<AuthService> logger,
        VernetztMetrics? metrics = null)
    {
        _userManager = userManager;
        _userRepository = userRepository;
        _emailService = emailService;
        _auditLogRepository = auditLogRepository;
        _dbContext = dbContext;
        _cache = cache;
        _logger = logger;
        _metrics = metrics;
        _contextEmailLoginHintEnabled = bool.Parse(configuration["Auth:ContextEmailLoginHint:Enabled"] ?? "true");
        _contextEmailLoginHintCooldownMinutes = int.Parse(configuration["Auth:ContextEmailLoginHintCooldownMinutes"] ?? "60");
        _loginLinkExpirationMinutes = LoginLinkTokens.GetExpirationMinutes(configuration);
        _loginLinkMaxPerUserPerHour = int.Parse(configuration["Auth:LoginLinkMaxPerUserPerHour"] ?? "5");
    }

    public async Task<LoginLinkToken?> CreateLoginLinkTokenAsync(
        string email,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = EmailAddressHelper.Normalize(email);
        var user = await _userRepository.GetByEmailAsync(email, cancellationToken);
        if (user == null)
        {
            _logger.LogWarning("Login link requested for unknown email {EmailMasked}", EmailAddressHelper.Mask(email));
            _metrics?.LoginLinkRejected(VernetztMetrics.RejectReasons.UnknownEmail);

            await _auditLogRepository.LogAsync(
                entityType: "AuthLogin",
                entityId: Guid.Empty,
                action: Core.Enums.AuditAction.LoginAttemptFailed,
                userId: Guid.Empty,
                oldValues: null,
                newValues: JsonSerializer.Serialize(new
                {
                    Reason = "UnknownEmail",
                    RequestedEmailMasked = EmailAddressHelper.Mask(email),
                    RequestedEmailHash = string.IsNullOrWhiteSpace(normalizedEmail)
                        ? null
                        : EmailAddressHelper.Sha256(normalizedEmail),
                    IpAddress = ipAddress
                }),
                cancellationToken: cancellationToken);

            if (_contextEmailLoginHintEnabled && !string.IsNullOrWhiteSpace(normalizedEmail))
            {
                await TrySendContextEmailLoginHintAsync(email, normalizedEmail, cancellationToken);
            }

            return null;
        }

        // The per-IP rate limit does not protect a single mailbox from being flooded from many IPs
        if (!TryConsumeLoginLinkBudget(user.Id))
        {
            _logger.LogWarning("Login link budget exhausted for user {UserId}; no email sent", user.Id);
            _metrics?.LoginLinkRejected(VernetztMetrics.RejectReasons.BudgetExhausted);
            return null;
        }

        return await IssueTokenAsync(user, cancellationToken);
    }

    /// <summary>
    /// Fixed one-hour window per user (in-memory, per instance). Returns false when the budget is used up.
    /// </summary>
    private bool TryConsumeLoginLinkBudget(Guid userId)
    {
        var counter = _cache.GetOrCreate($"login-link-budget:{userId}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            return new StrongBox<int>(0);
        })!;

        return Interlocked.Increment(ref counter.Value) <= _loginLinkMaxPerUserPerHour;
    }

    public async Task<LoginLinkToken?> CreateLoginLinkTokenForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        return user == null ? null : await IssueTokenAsync(user, cancellationToken);
    }

    public async Task<bool> RedeemLoginLinkTokenAsync(
        Guid userId,
        string token,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        // Convert.FromBase64String ignores whitespace, so " token" would verify like "token" but hash differently.
        // Accept only the canonical encoding and key single use on the decoded bytes.
        var tokenHash = CanonicalTokenHash(token);
        var account = tokenHash == null ? null : await _userManager.FindByIdAsync(userId.ToString());
        if (tokenHash == null || account == null ||
            !await _userManager.VerifyUserTokenAsync(account, LoginLinkTokens.ProviderName, LoginLinkTokens.Purpose, token))
        {
            await LogFailedRedeemAsync(userId, "InvalidOrExpired", ipAddress, cancellationToken);
            return false;
        }

        // Identity tokens are valid until they expire; enforce single use on top.
        if (await _dbContext.ConsumedLoginTokens.AnyAsync(t => t.TokenHash == tokenHash, cancellationToken))
        {
            await LogFailedRedeemAsync(userId, "AlreadyUsed", ipAddress, cancellationToken);
            return false;
        }

        _dbContext.ConsumedLoginTokens.Add(new ConsumedLoginToken
        {
            Id = Guid.NewGuid(),
            TokenHash = tokenHash,
            UserId = userId,
            ConsumedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(_loginLinkExpirationMinutes)
        });

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Unique index on TokenHash: a parallel request redeemed the same link first.
            await LogFailedRedeemAsync(userId, "AlreadyUsed", ipAddress, cancellationToken);
            return false;
        }

        return true;
    }

    public Task<bool> SendLoginLinkEmailAsync(string email, string loginLinkUrl, CancellationToken cancellationToken = default)
        => _emailService.SendMagicLinkEmailAsync(email, loginLinkUrl, _loginLinkExpirationMinutes, cancellationToken);

    public async Task RecordLoginAsync(Guid userId, string method, string? ipAddress, CancellationToken cancellationToken = default)
    {
        await _userRepository.UpdateLastLoginAsync(userId, cancellationToken);
        await _auditLogRepository.LogAsync(
            entityType: "User",
            entityId: userId,
            action: Core.Enums.AuditAction.Login,
            userId: userId,
            oldValues: null,
            newValues: JsonSerializer.Serialize(new { Method = method, IpAddress = ipAddress }),
            cancellationToken: cancellationToken);
    }

    public Task RecordLogoutAsync(Guid userId, bool allDevices, CancellationToken cancellationToken = default)
        => _auditLogRepository.LogAsync(
            entityType: "User",
            entityId: userId,
            action: Core.Enums.AuditAction.Logout,
            userId: userId,
            oldValues: null,
            newValues: allDevices ? JsonSerializer.Serialize(new { Scope = "AllDevices" }) : null,
            cancellationToken: cancellationToken);

    public async Task EnsureIdentityAccountAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (await _dbContext.IdentityAccounts.AnyAsync(a => a.Id == userId, cancellationToken))
        {
            return;
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user == null)
        {
            return;
        }

        _dbContext.IdentityAccounts.Add(AppIdentityUser.CreateFor(user));
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Created missing identity account for user {UserId}", userId);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetPasskeyCountsAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.UserPasskeys
            .AsNoTracking()
            .GroupBy(p => p.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PasskeyResetResult> ResetPasskeysAsync(Guid requestingUserId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (requestingUserId == userId)
        {
            return new PasskeyResetResult(PasskeyResetStatus.OwnAccount);
        }

        var requestingUser = await _userRepository.GetByIdAsync(requestingUserId, cancellationToken);
        if (requestingUser == null || !requestingUser.Claims.Contains(Core.Enums.UserClaim.SystemAdmin))
        {
            _logger.LogWarning("User {RequestingUserId} attempted to reset passkeys of {UserId} without permission", requestingUserId, userId);
            return new PasskeyResetResult(PasskeyResetStatus.Forbidden);
        }

        if (await _userRepository.GetByIdAsync(userId, cancellationToken) == null)
        {
            return new PasskeyResetResult(PasskeyResetStatus.NotFound);
        }

        await EnsureIdentityAccountAsync(userId, cancellationToken);
        var account = await _userManager.FindByIdAsync(userId.ToString());
        if (account == null)
        {
            return new PasskeyResetResult(PasskeyResetStatus.NotFound);
        }

        var removed = 0;
        foreach (var passkey in await _userManager.GetPasskeysAsync(account))
        {
            var result = await _userManager.RemovePasskeyAsync(account, passkey.CredentialId);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Passkey of user {userId} could not be removed: {string.Join(", ", result.Errors.Select(e => e.Code))}");
            }

            removed++;
        }

        // A lost or stolen device may still hold a session: end all sessions of the user.
        await _userManager.UpdateSecurityStampAsync(account);

        await _auditLogRepository.LogAsync(
            entityType: "User",
            entityId: userId,
            action: Core.Enums.AuditAction.Update,
            userId: requestingUserId,
            oldValues: null,
            newValues: JsonSerializer.Serialize(new { Action = "PasskeysReset", RemovedPasskeys = removed }),
            cancellationToken: cancellationToken);
        _logger.LogInformation("User {RequestingUserId} reset {Count} passkey(s) of user {UserId}", requestingUserId, removed, userId);

        return new PasskeyResetResult(PasskeyResetStatus.Done, removed);
    }

    private async Task<LoginLinkToken> IssueTokenAsync(User user, CancellationToken cancellationToken)
    {
        await EnsureIdentityAccountAsync(user.Id, cancellationToken);
        var account = await _userManager.FindByIdAsync(user.Id.ToString())
            ?? throw new InvalidOperationException($"Identity account for user {user.Id} could not be loaded.");

        var token = await _userManager.GenerateUserTokenAsync(account, LoginLinkTokens.ProviderName, LoginLinkTokens.Purpose);
        _logger.LogInformation("Login link token issued for user {UserId}", user.Id);
        _metrics?.LoginLinkIssued();
        return new LoginLinkToken(user.Id, user.Email, token);
    }

    private Task LogFailedRedeemAsync(Guid userId, string reason, string? ipAddress, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Login link rejected for user {UserId}: {Reason}", userId, reason);
        _metrics?.LoginLinkRejected(reason switch
        {
            "AlreadyUsed" => VernetztMetrics.RejectReasons.AlreadyUsed,
            _ => VernetztMetrics.RejectReasons.InvalidOrExpired
        });
        return _auditLogRepository.LogAsync(
            entityType: "AuthLogin",
            entityId: userId,
            action: Core.Enums.AuditAction.LoginAttemptFailed,
            userId: Guid.Empty,
            oldValues: null,
            newValues: JsonSerializer.Serialize(new { Reason = reason, IpAddress = ipAddress }),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Returns the hash of the decoded token bytes, or null if the token is not canonical Base64.
    /// </summary>
    private static string? CanonicalTokenHash(string token)
    {
        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(token);
        }
        catch (FormatException)
        {
            return null;
        }

        if (!string.Equals(Convert.ToBase64String(raw), token, StringComparison.Ordinal))
        {
            return null;
        }

        return Convert.ToHexString(SHA256.HashData(raw));
    }

    private async Task TrySendContextEmailLoginHintAsync(
        string requestedEmail,
        string normalizedRequestedEmail,
        CancellationToken cancellationToken)
    {
        var assignment = await _dbContext.ContactAssignments
            .AsNoTracking()
            .FirstOrDefaultAsync(
                a => a.ContextEmailVerified && a.ContextEmailNormalized == normalizedRequestedEmail,
                cancellationToken);

        if (assignment == null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var emailHash = EmailAddressHelper.Sha256(normalizedRequestedEmail);
        var rateLimit = await _dbContext.LoginEmailHintRateLimits
            .FirstOrDefaultAsync(r => r.EmailHash == emailHash, cancellationToken);

        if (rateLimit != null)
        {
            var nextAllowedAt = rateLimit.LastSentAt.AddMinutes(_contextEmailLoginHintCooldownMinutes);
            if (nextAllowedAt > now)
            {
                _logger.LogInformation(
                    "Skipping context-email login hint due cooldown. Email={EmailMasked}, NextAllowedAt={NextAllowedAt}",
                    EmailAddressHelper.Mask(requestedEmail),
                    nextAllowedAt);
                return;
            }

            rateLimit.LastSentAt = now;
        }
        else
        {
            rateLimit = new LoginEmailHintRateLimit
            {
                Id = Guid.NewGuid(),
                EmailHash = emailHash,
                LastSentAt = now
            };
            await _dbContext.LoginEmailHintRateLimits.AddAsync(rateLimit, cancellationToken);
        }

        var sent = await _emailService.SendContextEmailLoginHintAsync(requestedEmail, cancellationToken);

        if (!sent)
        {
            _logger.LogWarning(
                "Context-email login hint could not be sent. Email={EmailMasked}",
                EmailAddressHelper.Mask(requestedEmail));
            return;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditLogRepository.LogAsync(
            "LoginEmailHint",
            assignment.Id,
            Core.Enums.AuditAction.Update,
            assignment.UserId,
            null,
            "Context email login hint sent (rate-limited).",
            cancellationToken);
    }
}
