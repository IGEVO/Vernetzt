using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Igevo.Vernetzt.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of ContactPosition repository
/// </summary>
public class ContactPositionRepository : EfCoreRepository<ContactPosition>, IContactPositionRepository
{
    public ContactPositionRepository(VernetztDbContext context, ILogger<ContactPositionRepository> logger)
        : base(context, logger)
    {
    }

    public async Task<ContactPosition> CreateAsync(ContactPosition contactPosition, CancellationToken cancellationToken = default)
    {
        return await AddAsync(contactPosition, cancellationToken);
    }
}
