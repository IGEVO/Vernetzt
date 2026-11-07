using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Igevo.Vernetzt.Infrastructure.Repositories;

/// <summary>
/// EF Core repository implementation for ClubType entities.
/// </summary>
public class ClubTypeRepository : EfCoreRepository<ClubType>, IClubTypeRepository
{
    public ClubTypeRepository(VernetztDbContext context, ILogger<ClubTypeRepository> logger)
        : base(context, logger)
    {
    }

    public async Task<ClubType?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(ct => ct.Name.ToLower() == name.ToLower(), cancellationToken);
    }
}
