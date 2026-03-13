using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class PostMetricSnapshotRepository : IPostMetricSnapshotRepository
{
    private readonly MyDbContext _dbContext;
    private readonly DbSet<PostMetricSnapshot> _dbSet;

    public PostMetricSnapshotRepository(MyDbContext dbContext)
    {
        _dbContext = dbContext;
        _dbSet = dbContext.Set<PostMetricSnapshot>();
    }

    public async Task<PostMetricSnapshot?> GetLatestAsync(
        Guid userId,
        Guid socialMediaId,
        string platformPostId,
        CancellationToken cancellationToken)
    {
        return await _dbSet
            .AsNoTracking()
            .FirstOrDefaultAsync(
                snapshot => snapshot.UserId == userId &&
                            snapshot.SocialMediaId == socialMediaId &&
                            snapshot.PlatformPostId == platformPostId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<PostMetricSnapshot>> GetLatestByPlatformPostIdsAsync(
        Guid userId,
        Guid socialMediaId,
        IReadOnlyList<string> platformPostIds,
        CancellationToken cancellationToken)
    {
        if (platformPostIds.Count == 0)
        {
            return Array.Empty<PostMetricSnapshot>();
        }

        var ids = platformPostIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (ids.Length == 0)
        {
            return Array.Empty<PostMetricSnapshot>();
        }

        return await _dbSet
            .AsNoTracking()
            .Where(snapshot => snapshot.UserId == userId &&
                               snapshot.SocialMediaId == socialMediaId &&
                               ids.Contains(snapshot.PlatformPostId))
            .ToListAsync(cancellationToken);
    }

    public async Task<PostMetricSnapshot?> GetLatestForUpdateAsync(
        Guid userId,
        Guid socialMediaId,
        string platformPostId,
        CancellationToken cancellationToken)
    {
        return await _dbSet.FirstOrDefaultAsync(
            snapshot => snapshot.UserId == userId &&
                        snapshot.SocialMediaId == socialMediaId &&
                        snapshot.PlatformPostId == platformPostId,
            cancellationToken);
    }

    public Task AddAsync(PostMetricSnapshot entity, CancellationToken cancellationToken)
    {
        return _dbSet.AddAsync(entity, cancellationToken).AsTask();
    }

    public void Update(PostMetricSnapshot entity)
    {
        _dbSet.Update(entity);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}
