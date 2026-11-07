using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Igevo.Vernetzt.Infrastructure.Repositories;

/// <summary>
/// Generic repository implementation using Entity Framework Core.
/// </summary>
/// <typeparam name="T">Entity type that inherits from BaseEntity.</typeparam>
public class EfCoreRepository<T> : IRepository<T> where T : BaseEntity
{
    protected readonly VernetztDbContext _context;
    protected readonly ILogger<EfCoreRepository<T>> _logger;
    protected readonly DbSet<T> _dbSet;

    public EfCoreRepository(VernetztDbContext context, ILogger<EfCoreRepository<T>> logger)
    {
        _context = context;
        _logger = logger;
        _dbSet = _context.Set<T>();
    }

    public virtual async Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet.FindAsync([id], cancellationToken);
    }

    public virtual async Task<IEnumerable<T>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet.ToListAsync(cancellationToken);
    }

    public virtual async Task<T> AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        entity.Id = Guid.NewGuid();
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;

        await _dbSet.AddAsync(entity, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created {EntityType} with ID: {Id}", typeof(T).Name, entity.Id);
        return entity;
    }

    public virtual async Task<T> UpdateAsync(T entity, CancellationToken cancellationToken = default)
    {
        entity.UpdatedAt = DateTime.UtcNow;

        // Only call Update if entity is not already being tracked
        // This prevents issues with newly added child entities being marked as Modified
        var entry = _context.Entry(entity);
        if (entry.State == EntityState.Detached)
        {
            _dbSet.Update(entity);
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated {EntityType} with ID: {Id}", typeof(T).Name, entity.Id);
        return entity;
    }

    public virtual async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _dbSet.FindAsync([id], cancellationToken);
        if (entity == null)
        {
            return false;
        }

        _dbSet.Remove(entity);
        _logger.LogInformation("Deleted {EntityType} with ID: {Id}", typeof(T).Name, id);

        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public virtual async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet.AnyAsync(x => x.Id == id, cancellationToken);
    }
}
