using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class PostPublicationRepository : IPostPublicationRepository
{
    private readonly MyDbContext _dbContext;
    private readonly DbSet<PostPublication> _dbSet;

    public PostPublicationRepository(MyDbContext dbContext)
    {
        _dbContext = dbContext;
        _dbSet = dbContext.Set<PostPublication>();
    }

    public Task AddRangeAsync(IEnumerable<PostPublication> entities, CancellationToken cancellationToken)
    {
        return _dbSet.AddRangeAsync(entities, cancellationToken);
    }

    public async Task<IReadOnlyList<PostPublication>> GetByPostIdsAsync(
        IReadOnlyList<Guid> postIds,
        CancellationToken cancellationToken)
    {
        if (postIds.Count == 0)
        {
            return Array.Empty<PostPublication>();
        }

        return await _dbSet.AsNoTracking()
            .Where(publication => postIds.Contains(publication.PostId) && !publication.DeletedAt.HasValue)
            .OrderByDescending(publication => publication.PublishedAt)
            .ThenByDescending(publication => publication.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public Task<PostPublication?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return _dbSet.FirstOrDefaultAsync(publication => publication.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<PostPublication>> GetByPostIdForUpdateAsync(
        Guid postId,
        CancellationToken cancellationToken)
    {
        return await _dbSet
            .Where(publication => publication.PostId == postId && !publication.DeletedAt.HasValue)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PostPublication>> GetAllByPostIdIncludingDeletedAsync(
        Guid postId,
        CancellationToken cancellationToken)
    {
        return await _dbSet
            .AsNoTracking()
            .Where(publication => publication.PostId == postId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PostPublication>> GetBySocialMediaIdForUpdateAsync(
        Guid socialMediaId,
        CancellationToken cancellationToken)
    {
        return await _dbSet
            .Where(publication =>
                publication.SocialMediaId == socialMediaId &&
                !publication.DeletedAt.HasValue)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PostPublication>> GetByPostIdsIncludingDeletedForUpdateAsync(
        IReadOnlyList<Guid> postIds,
        CancellationToken cancellationToken)
    {
        if (postIds.Count == 0)
        {
            return Array.Empty<PostPublication>();
        }

        return await _dbSet
            .Where(publication => postIds.Contains(publication.PostId))
            .ToListAsync(cancellationToken);
    }

    public Task<PostPublication?> GetBySocialMediaAndExternalContentForUpdateAsync(
        Guid socialMediaId,
        string externalContentId,
        CancellationToken cancellationToken)
    {
        return _dbSet.FirstOrDefaultAsync(
            publication =>
                publication.SocialMediaId == socialMediaId &&
                publication.ExternalContentId == externalContentId,
            cancellationToken);
    }

    public Task<PostPublication?> GetByExternalContentKeyForUpdateAsync(
        string socialMediaType,
        string destinationOwnerId,
        string externalContentId,
        CancellationToken cancellationToken)
    {
        return _dbSet.FirstOrDefaultAsync(
            publication =>
                publication.SocialMediaType == socialMediaType &&
                publication.DestinationOwnerId == destinationOwnerId &&
                publication.ExternalContentId == externalContentId,
            cancellationToken);
    }

    public void Update(PostPublication entity)
    {
        _dbSet.Update(entity);
    }

    public void DeleteRange(IEnumerable<PostPublication> entities)
    {
        _dbSet.RemoveRange(entities);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}
