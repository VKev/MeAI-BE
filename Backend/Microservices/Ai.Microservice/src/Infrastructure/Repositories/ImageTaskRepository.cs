using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class ImageTaskRepository : IImageTaskRepository
{
    private readonly MyDbContext _dbContext;
    private readonly DbSet<ImageTask> _dbSet;

    public ImageTaskRepository(MyDbContext dbContext)
    {
        _dbContext = dbContext;
        _dbSet = dbContext.Set<ImageTask>();
    }

    public Task AddAsync(ImageTask entity, CancellationToken cancellationToken)
    {
        return _dbSet.AddAsync(entity, cancellationToken).AsTask();
    }

    public void Update(ImageTask entity)
    {
        _dbSet.Update(entity);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ImageTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbSet.FindAsync(new object?[] { id }, cancellationToken);
    }

    public async Task<ImageTask?> GetByCorrelationIdAsync(Guid correlationId, CancellationToken cancellationToken)
    {
        return await _dbSet.AsNoTracking().FirstOrDefaultAsync(t => t.CorrelationId == correlationId, cancellationToken);
    }

    public async Task<ImageTask?> GetByCorrelationIdForUpdateAsync(Guid correlationId, CancellationToken cancellationToken)
    {
        return await _dbSet.FirstOrDefaultAsync(t => t.CorrelationId == correlationId, cancellationToken);
    }

    public async Task<ImageTask?> GetByKieTaskIdAsync(string kieTaskId, CancellationToken cancellationToken)
    {
        return await _dbSet.FirstOrDefaultAsync(t => t.KieTaskId == kieTaskId, cancellationToken);
    }

    public async Task<IEnumerable<ImageTask>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _dbSet.AsNoTracking().Where(t => t.UserId == userId).ToListAsync(cancellationToken);
    }
}
