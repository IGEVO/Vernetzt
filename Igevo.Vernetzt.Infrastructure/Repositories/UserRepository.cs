using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Igevo.Vernetzt.Infrastructure.Repositories;

/// <summary>
/// EF Core repository implementation for User entities.
/// </summary>
public class UserRepository : EfCoreRepository<User>, IUserRepository
{
    public UserRepository(VernetztDbContext context, ILogger<UserRepository> logger)
        : base(context, logger)
    {
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        // User.Email is stored trimmed and lower-case (value converter in VernetztDbContext),
        // so normalizing the lookup value the same way makes the lookup case-insensitive.
        var normalized = email.Trim().ToLowerInvariant();
        return await _dbSet
            .FirstOrDefaultAsync(u => u.Email == normalized, cancellationToken);
    }

    public async Task UpdateLastLoginAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _dbSet.FindAsync([userId], cancellationToken);
        if (user != null)
        {
            user.LastLoginAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Updated last login for user: {UserId}", userId);
        }
    }

    /// <summary>
    /// Get user with all related contact assignments.
    /// </summary>
    public async Task<User?> GetByIdWithAssignmentsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(u => u.ContactAssignments)
                .ThenInclude(ca => ca.Club)
            .Include(u => u.ContactAssignments)
                .ThenInclude(ca => ca.Department)
            .Include(u => u.ContactAssignments)
                .ThenInclude(ca => ca.ContactPosition)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }
}
