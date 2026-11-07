using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Igevo.Vernetzt.Infrastructure.Repositories;

/// <summary>
/// EF Core repository implementation for InvitationToken entities.
/// </summary>
public class InvitationTokenRepository : IInvitationTokenRepository
{
    private readonly VernetztDbContext _context;

    public InvitationTokenRepository(VernetztDbContext context)
    {
        _context = context;
    }

    public async Task<InvitationToken?> GetByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        return await _context.InvitationTokens
            .FirstOrDefaultAsync(t => t.Token == token, cancellationToken);
    }

    public async Task<InvitationToken?> GetValidByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        return await _context.InvitationTokens
            .Where(t => t.Email == email && !t.IsUsed && !t.IsRevoked && t.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<InvitationToken>> GetAllValidByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        return await _context.InvitationTokens
            .Include(t => t.Club)
            .Include(t => t.Department)
                .ThenInclude(d => d!.Club)
            .Include(t => t.Department)
                .ThenInclude(d => d!.ParentDepartment)
            .Include(t => t.ContactPosition)
            .Where(t => t.Email == email && !t.IsUsed && !t.IsRevoked && t.ExpiresAt > DateTime.UtcNow)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<InvitationToken> AddAsync(InvitationToken invitationToken, CancellationToken cancellationToken = default)
    {
        _context.InvitationTokens.Add(invitationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return invitationToken;
    }

    public async Task<InvitationToken> UpdateAsync(InvitationToken invitationToken, CancellationToken cancellationToken = default)
    {
        // Only call Update if entity is not already being tracked
        // This prevents marking all properties as modified unnecessarily
        var entry = _context.Entry(invitationToken);
        if (entry.State == EntityState.Detached)
        {
            _context.InvitationTokens.Update(invitationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return invitationToken;
    }

    public async Task<int> DeleteExpiredAsync(DateTime cutoffDate, CancellationToken cancellationToken = default)
    {
        var expiredTokens = await _context.InvitationTokens
            .Where(t => (t.IsUsed || t.IsRevoked || t.ExpiresAt <= DateTime.UtcNow) && t.CreatedAt < cutoffDate)
            .ToListAsync(cancellationToken);

        if (expiredTokens.Count == 0)
            return 0;

        _context.InvitationTokens.RemoveRange(expiredTokens);
        await _context.SaveChangesAsync(cancellationToken);
        return expiredTokens.Count;
    }

    public async Task<int> DeleteByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var emailTokens = await _context.InvitationTokens
            .Where(t => t.Email == email)
            .ToListAsync(cancellationToken);

        if (emailTokens.Count == 0)
            return 0;

        _context.InvitationTokens.RemoveRange(emailTokens);
        await _context.SaveChangesAsync(cancellationToken);
        return emailTokens.Count;
    }
}
