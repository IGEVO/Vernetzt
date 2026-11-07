using Igevo.Vernetzt.Core.Entities;

namespace Igevo.Vernetzt.Core.Interfaces.Repositories;

/// <summary>
/// Repository interface for ContactPosition entity operations
/// </summary>
public interface IContactPositionRepository : IRepository<ContactPosition>
{
    Task<ContactPosition> CreateAsync(ContactPosition contactPosition, CancellationToken cancellationToken = default);
}
