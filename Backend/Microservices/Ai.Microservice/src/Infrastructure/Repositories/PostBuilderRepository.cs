using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class PostBuilderRepository : IPostBuilderRepository
{
    private readonly MyDbContext _dbContext;
    private readonly DbSet<PostBuilder> _dbSet;

    public PostBuilderRepository(MyDbContext dbContext)
    {
        _dbContext = dbContext;
        _dbSet = dbContext.Set<PostBuilder>();
    }

    public Task AddAsync(PostBuilder entity, CancellationToken cancellationToken)
    {
        return _dbSet.AddAsync(entity, cancellationToken).AsTask();
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<PostBuilder?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbSet
            .AsNoTracking()
            .Include(builder => builder.Posts)
            .FirstOrDefaultAsync(builder => builder.Id == id, cancellationToken);
    }

    public async Task<PostBuilder?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbSet
            .Include(builder => builder.Posts)
            .FirstOrDefaultAsync(builder => builder.Id == id, cancellationToken);
    }
}
