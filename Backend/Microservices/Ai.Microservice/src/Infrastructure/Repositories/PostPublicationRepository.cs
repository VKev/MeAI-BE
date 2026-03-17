using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class PostPublicationRepository : IPostPublicationRepository
{
    private readonly DbSet<PostPublication> _dbSet;

    public PostPublicationRepository(MyDbContext dbContext)
    {
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
}
