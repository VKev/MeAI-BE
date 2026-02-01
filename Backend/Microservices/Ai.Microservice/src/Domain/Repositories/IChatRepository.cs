using Domain.Entities;

namespace Domain.Repositories;

public interface IChatRepository
{
    Task AddAsync(Chat entity, CancellationToken cancellationToken);
    void Update(Chat entity);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
    Task<Chat?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Chat?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken);
    Task<IEnumerable<Chat>> GetBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken);
}
