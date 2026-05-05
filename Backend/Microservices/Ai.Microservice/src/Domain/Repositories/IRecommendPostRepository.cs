using Domain.Entities;

namespace Domain.Repositories;

public interface IRecommendPostRepository
{
    Task AddAsync(RecommendPost entity, CancellationToken cancellationToken);

    Task<RecommendPost?> GetByCorrelationIdAsync(Guid correlationId, CancellationToken cancellationToken);

    Task<RecommendPost?> GetByCorrelationIdForUpdateAsync(Guid correlationId, CancellationToken cancellationToken);

    /// <summary>Looks up the active RecommendPost for an OriginalPostId, if any.
    /// Used by the GET endpoint and (read-only) by the start command's pre-flight.
    /// Replace-on-rerun deletion uses <see cref="GetByOriginalPostIdForUpdateAsync"/>.</summary>
    Task<RecommendPost?> GetByOriginalPostIdAsync(Guid originalPostId, CancellationToken cancellationToken);

    /// <summary>Tracked variant — the start command uses this to fetch the existing
    /// row before hard-deleting it under replace-on-rerun semantics.</summary>
    Task<RecommendPost?> GetByOriginalPostIdForUpdateAsync(Guid originalPostId, CancellationToken cancellationToken);

    void Update(RecommendPost entity);

    void Remove(RecommendPost entity);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
