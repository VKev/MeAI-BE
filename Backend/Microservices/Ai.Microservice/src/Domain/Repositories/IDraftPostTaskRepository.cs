using Domain.Entities;

namespace Domain.Repositories;

public interface IDraftPostTaskRepository
{
    Task AddAsync(DraftPostTask entity, CancellationToken cancellationToken);

    Task<DraftPostTask?> GetByCorrelationIdAsync(Guid correlationId, CancellationToken cancellationToken);

    Task<DraftPostTask?> GetByCorrelationIdOrResultPostIdAsync(Guid id, CancellationToken cancellationToken);

    Task<DraftPostTask?> GetByCorrelationIdForUpdateAsync(Guid correlationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<DraftPostTask>> GetByResultPostIdsAsync(
        IReadOnlyList<Guid> postIds,
        CancellationToken cancellationToken);

    void Update(DraftPostTask entity);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
