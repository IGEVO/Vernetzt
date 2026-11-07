using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Igevo.Vernetzt.Infrastructure.Repositories;

/// <summary>
/// EF Core repository implementation for EmailRelayToken entities.
/// </summary>
public class EmailRelayTokenRepository : IEmailRelayTokenRepository
{
    private readonly VernetztDbContext _context;

    public EmailRelayTokenRepository(VernetztDbContext context)
    {
        _context = context;
    }

    public async Task<EmailRelayToken?> GetActiveTokenByAddressAsync(string tokenAddress, CancellationToken cancellationToken = default)
    {
        var normalised = tokenAddress.ToLowerInvariant();
        return await _context.EmailRelayTokens
            .FirstOrDefaultAsync(t =>
                t.TokenAddress.ToLower() == normalised &&
                !t.IsUsed &&
                t.ExpiresAt > DateTime.UtcNow,
                cancellationToken);
    }

    public async Task<EmailRelayToken?> GetTokenByAddressAsync(string tokenAddress, CancellationToken cancellationToken = default)
    {
        var normalised = tokenAddress.ToLowerInvariant();
        return await _context.EmailRelayTokens
            .FirstOrDefaultAsync(t => t.TokenAddress.ToLower() == normalised, cancellationToken);
    }

    public async Task<List<EmailRelayToken>> GetActiveTokensByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.EmailRelayTokens
            .Where(t =>
                t.CreatedByUserId == userId &&
                !t.IsUsed &&
                t.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<EmailRelayToken>> GetExpiredTokensAsync(CancellationToken cancellationToken = default)
    {
        return await _context.EmailRelayTokens
            .Where(t =>
                !t.IsUsed &&
                t.ExpiresAt <= DateTime.UtcNow)
            .ToListAsync(cancellationToken);
    }

    public async Task<EmailRelayToken?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.EmailRelayTokens
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<EmailRelayToken> AddAsync(EmailRelayToken token, CancellationToken cancellationToken = default)
    {
        _context.EmailRelayTokens.Add(token);
        await _context.SaveChangesAsync(cancellationToken);
        return token;
    }

    public async Task<EmailRelayToken> UpdateAsync(EmailRelayToken token, CancellationToken cancellationToken = default)
    {
        var entry = _context.Entry(token);
        if (entry.State == EntityState.Detached)
        {
            _context.EmailRelayTokens.Update(token);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return token;
    }

    public async Task DeleteAsync(EmailRelayToken token, CancellationToken cancellationToken = default)
    {
        _context.EmailRelayTokens.Remove(token);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> CountActiveTokensByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.EmailRelayTokens
            .CountAsync(t =>
                t.CreatedByUserId == userId &&
                !t.IsUsed &&
                t.ExpiresAt > DateTime.UtcNow,
                cancellationToken);
    }

    public async Task<int> DeleteExpiredAsync(DateTime cutoffDate, CancellationToken cancellationToken = default)
    {
        var expiredTokens = await _context.EmailRelayTokens
            .Where(t => (t.IsUsed || t.ExpiresAt <= DateTime.UtcNow) && t.CreatedAt < cutoffDate)
            .ToListAsync(cancellationToken);

        if (expiredTokens.Count == 0)
            return 0;

        _context.EmailRelayTokens.RemoveRange(expiredTokens);
        await _context.SaveChangesAsync(cancellationToken);
        return expiredTokens.Count;
    }

    public async Task<int> DeleteByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var userTokens = await _context.EmailRelayTokens
            .Where(t => t.CreatedByUserId == userId)
            .ToListAsync(cancellationToken);

        if (userTokens.Count == 0)
            return 0;

        _context.EmailRelayTokens.RemoveRange(userTokens);
        await _context.SaveChangesAsync(cancellationToken);
        return userTokens.Count;
    }
}
