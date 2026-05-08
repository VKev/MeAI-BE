using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class DraftPostTaskRepository : IDraftPostTaskRepository
{
    private readonly MyDbContext _dbContext;
    private readonly DbSet<DraftPostTask> _dbSet;

    public DraftPostTaskRepository(MyDbContext dbContext)
    {
        _dbContext = dbContext;
        _dbSet = dbContext.Set<DraftPostTask>();
    }

    public Task AddAsync(DraftPostTask entity, CancellationToken cancellationToken)
        => _dbSet.AddAsync(entity, cancellationToken).AsTask();

    public Task<DraftPostTask?> GetByCorrelationIdAsync(Guid correlationId, CancellationToken cancellationToken)
        => _dbSet.AsNoTracking().FirstOrDefaultAsync(t => t.CorrelationId == correlationId, cancellationToken);

    public Task<DraftPostTask?> GetByCorrelationIdForUpdateAsync(Guid correlationId, CancellationToken cancellationToken)
        => _dbSet.FirstOrDefaultAsync(t => t.CorrelationId == correlationId, cancellationToken);

    public async Task<IReadOnlyList<DraftPostTask>> GetByResultPostIdsAsync(
        IReadOnlyList<Guid> postIds,
        CancellationToken cancellationToken)
    {
        if (postIds.Count == 0)
        {
            return Array.Empty<DraftPostTask>();
        }

        return await _dbSet
            .AsNoTracking()
            .Where(task => task.ResultPostId.HasValue && postIds.Contains(task.ResultPostId.Value))
            .ToListAsync(cancellationToken);
    }

    public void Update(DraftPostTask entity) => _dbSet.Update(entity);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        => _dbContext.SaveChangesAsync(cancellationToken);
}
