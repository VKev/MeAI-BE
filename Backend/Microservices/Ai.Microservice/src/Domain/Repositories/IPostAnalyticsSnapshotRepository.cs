using Domain.Entities;

namespace Domain.Repositories;

public interface IPostAnalyticsSnapshotRepository
{
    Task<PostAnalyticsSnapshot?> GetLatestAsync(
        Guid userId,
        Guid socialMediaId,
        string platformPostId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PostAnalyticsSnapshot>> GetLatestByPlatformPostIdsAsync(
        Guid userId,
        Guid socialMediaId,
        IReadOnlyList<string> platformPostIds,
        CancellationToken cancellationToken);

    Task<PostAnalyticsSnapshot?> GetLatestForUpdateAsync(
        Guid userId,
        Guid socialMediaId,
        string platformPostId,
        CancellationToken cancellationToken);

    Task AddAsync(PostAnalyticsSnapshot entity, CancellationToken cancellationToken);

    void Update(PostAnalyticsSnapshot entity);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
