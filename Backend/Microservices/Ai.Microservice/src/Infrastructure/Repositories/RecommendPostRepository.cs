using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class RecommendPostRepository : IRecommendPostRepository
{
    private readonly MyDbContext _dbContext;
    private readonly DbSet<RecommendPost> _dbSet;

    public RecommendPostRepository(MyDbContext dbContext)
    {
        _dbContext = dbContext;
        _dbSet = dbContext.Set<RecommendPost>();
    }

    public Task AddAsync(RecommendPost entity, CancellationToken cancellationToken)
        => _dbSet.AddAsync(entity, cancellationToken).AsTask();

    public Task<RecommendPost?> GetByCorrelationIdAsync(Guid correlationId, CancellationToken cancellationToken)
        => _dbSet.AsNoTracking().FirstOrDefaultAsync(t => t.CorrelationId == correlationId, cancellationToken);

    public Task<RecommendPost?> GetByCorrelationIdForUpdateAsync(Guid correlationId, CancellationToken cancellationToken)
        => _dbSet.FirstOrDefaultAsync(t => t.CorrelationId == correlationId, cancellationToken);

    public Task<RecommendPost?> GetByOriginalPostIdAsync(Guid originalPostId, CancellationToken cancellationToken)
        => _dbSet.AsNoTracking().FirstOrDefaultAsync(t => t.OriginalPostId == originalPostId, cancellationToken);

    public Task<RecommendPost?> GetByOriginalPostIdForUpdateAsync(Guid originalPostId, CancellationToken cancellationToken)
        => _dbSet.FirstOrDefaultAsync(t => t.OriginalPostId == originalPostId, cancellationToken);

    public void Update(RecommendPost entity) => _dbSet.Update(entity);

    public void Remove(RecommendPost entity) => _dbSet.Remove(entity);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        => _dbContext.SaveChangesAsync(cancellationToken);
}
