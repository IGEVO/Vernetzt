using Igevo.Vernetzt.Core.Configuration;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Igevo.Vernetzt.Infrastructure.Services;

/// <summary>
/// Service that cleans up expired/used data from the database.
/// </summary>
public class DataCleanupService : IDataCleanupService
{
    private readonly VernetztDbContext _context;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IInvitationTokenRepository _invitationTokenRepository;
    private readonly IEmailRelayTokenRepository _emailRelayTokenRepository;
    private readonly AppSettings _appSettings;
    private readonly ILogger<DataCleanupService> _logger;

    public DataCleanupService(
        VernetztDbContext context,
        IAuditLogRepository auditLogRepository,
        IInvitationTokenRepository invitationTokenRepository,
        IEmailRelayTokenRepository emailRelayTokenRepository,
        IOptions<AppSettings> appSettings,
        ILogger<DataCleanupService> logger)
    {
        _context = context;
        _auditLogRepository = auditLogRepository;
        _invitationTokenRepository = invitationTokenRepository;
        _emailRelayTokenRepository = emailRelayTokenRepository;
        _appSettings = appSettings.Value;
        _logger = logger;
    }

    public async Task<DataCleanupResult> RunCleanupAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting data cleanup job...");

        var result = new DataCleanupResult();

        try
        {
            // 1. Clean up old audit logs
            var auditCutoff = DateTime.UtcNow.AddDays(-_appSettings.AuditLogRetentionDays);
            result.DeletedAuditLogs = await _auditLogRepository.DeleteOlderThanAsync(auditCutoff, cancellationToken);

            // 2. Clean up expired/used invitation tokens
            var tokenCutoff = DateTime.UtcNow.AddDays(-_appSettings.ExpiredTokenRetentionDays);
            result.DeletedInvitationTokens = await _invitationTokenRepository.DeleteExpiredAsync(tokenCutoff, cancellationToken);

            // 3. Clean up expired/used email relay tokens
            result.DeletedEmailRelayTokens = await _emailRelayTokenRepository.DeleteExpiredAsync(tokenCutoff, cancellationToken);

            // 4. Clean up redeemed login-link tokens once they would have expired anyway
            result.DeletedConsumedLoginTokens = await CleanupConsumedLoginTokensAsync(cancellationToken);

            // 5. Clean up revoked CardDAV app passwords (they are kept a while for the audit trail)
            result.DeletedCardDavTokens = await _context.CardDavTokens
                .Where(t => t.IsRevoked && t.RevokedAt != null && t.RevokedAt < tokenCutoff)
                .ExecuteDeleteAsync(cancellationToken);

            _logger.LogInformation(
                "Data cleanup completed. Deleted: AuditLogs={AuditLogs}, InvitationTokens={InvitationTokens}, " +
                "EmailRelayTokens={EmailRelayTokens}, ConsumedLoginTokens={ConsumedLoginTokens}, " +
                "CardDavTokens={CardDavTokens}, Total={Total}",
                result.DeletedAuditLogs, result.DeletedInvitationTokens,
                result.DeletedEmailRelayTokens, result.DeletedConsumedLoginTokens, result.DeletedCardDavTokens,
                result.TotalDeleted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during data cleanup job");
            throw;
        }

        return result;
    }

    private async Task<int> CleanupConsumedLoginTokensAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var expiredTokens = await _context.ConsumedLoginTokens
            .Where(t => t.ExpiresAt <= now)
            .ToListAsync(cancellationToken);

        if (expiredTokens.Count == 0)
            return 0;

        _context.ConsumedLoginTokens.RemoveRange(expiredTokens);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogDebug("Deleted {Count} expired consumed login tokens", expiredTokens.Count);
        return expiredTokens.Count;
    }
}
