using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class PostRepository : IPostRepository
{
    private readonly MyDbContext _dbContext;
    private readonly DbSet<Post> _dbSet;

    public PostRepository(MyDbContext dbContext)
    {
        _dbContext = dbContext;
        _dbSet = dbContext.Set<Post>();
    }

    public Task AddAsync(Post entity, CancellationToken cancellationToken)
    {
        return _dbSet.AddAsync(entity, cancellationToken).AsTask();
    }

    public void Update(Post entity)
    {
        _dbSet.Update(entity);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<Post?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbSet.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<Post?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbSet.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Post>> GetByUserIdAsync(
        Guid userId,
        DateTime? cursorCreatedAt,
        Guid? cursorId,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = _dbSet.AsNoTracking()
            .Where(p => p.UserId == userId && p.DeletedAt == null);

        if (cursorCreatedAt.HasValue && cursorId.HasValue)
        {
            var createdAt = cursorCreatedAt.Value;
            var lastId = cursorId.Value;
            query = query.Where(post =>
                (post.CreatedAt < createdAt) ||
                (post.CreatedAt == createdAt && post.Id.CompareTo(lastId) < 0));
        }

        return await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Post>> GetTrackedByPostBuilderIdAsync(
        Guid postBuilderId,
        CancellationToken cancellationToken)
    {
        return await _dbSet
            .Where(p => p.PostBuilderId == postBuilderId && p.DeletedAt == null)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Post>> GetByUserIdAndWorkspaceIdAsync(
        Guid userId,
        Guid workspaceId,
        DateTime? cursorCreatedAt,
        Guid? cursorId,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = _dbSet.AsNoTracking()
            .Where(p => p.UserId == userId &&
                        p.WorkspaceId == workspaceId &&
                        p.DeletedAt == null);

        if (cursorCreatedAt.HasValue && cursorId.HasValue)
        {
            var createdAt = cursorCreatedAt.Value;
            var lastId = cursorId.Value;
            query = query.Where(post =>
                (post.CreatedAt < createdAt) ||
                (post.CreatedAt == createdAt && post.Id.CompareTo(lastId) < 0));
        }

        return await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
