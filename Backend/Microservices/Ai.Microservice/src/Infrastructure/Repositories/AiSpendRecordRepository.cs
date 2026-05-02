using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class AiSpendRecordRepository : IAiSpendRecordRepository
{
    private readonly MyDbContext _dbContext;
    private readonly DbSet<AiSpendRecord> _dbSet;

    public AiSpendRecordRepository(MyDbContext dbContext)
    {
        _dbContext = dbContext;
        _dbSet = dbContext.Set<AiSpendRecord>();
    }

    public Task AddAsync(AiSpendRecord entity, CancellationToken cancellationToken)
    {
        return _dbSet.AddAsync(entity, cancellationToken).AsTask();
    }

    public async Task AddRangeAsync(IEnumerable<AiSpendRecord> entities, CancellationToken cancellationToken)
    {
        await _dbSet.AddRangeAsync(entities, cancellationToken);
    }

    public void Update(AiSpendRecord entity)
    {
        _dbSet.Update(entity);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AiSpendRecord>> GetCreatedBetweenAsync(
        DateTime startInclusive,
        DateTime endExclusive,
        CancellationToken cancellationToken)
    {
        return await _dbSet.AsNoTracking()
            .Where(record => record.CreatedAt >= startInclusive && record.CreatedAt < endExclusive)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AiSpendRecord>> GetByReferenceAsync(
        string referenceType,
        string referenceId,
        CancellationToken cancellationToken)
    {
        return await _dbSet
            .Where(record => record.ReferenceType == referenceType && record.ReferenceId == referenceId)
            .ToListAsync(cancellationToken);
    }
}
