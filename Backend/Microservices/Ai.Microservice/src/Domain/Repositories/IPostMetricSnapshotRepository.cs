using Domain.Entities;

namespace Domain.Repositories;

public interface IPostMetricSnapshotRepository
{
    Task<PostMetricSnapshot?> GetLatestAsync(
        Guid userId,
        Guid socialMediaId,
        string platformPostId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PostMetricSnapshot>> GetLatestByPlatformPostIdsAsync(
        Guid userId,
        Guid socialMediaId,
        IReadOnlyList<string> platformPostIds,
        CancellationToken cancellationToken);

    Task<PostMetricSnapshot?> GetLatestForUpdateAsync(
        Guid userId,
        Guid socialMediaId,
        string platformPostId,
        CancellationToken cancellationToken);

    Task AddAsync(PostMetricSnapshot entity, CancellationToken cancellationToken);

    void Update(PostMetricSnapshot entity);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
