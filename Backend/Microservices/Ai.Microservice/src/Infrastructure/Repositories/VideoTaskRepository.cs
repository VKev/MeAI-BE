using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class VideoTaskRepository : IVideoTaskRepository
{
    private readonly MyDbContext _dbContext;
    private readonly DbSet<VideoTask> _dbSet;

    public VideoTaskRepository(MyDbContext dbContext)
    {
        _dbContext = dbContext;
        _dbSet = dbContext.Set<VideoTask>();
    }

    public Task AddAsync(VideoTask entity, CancellationToken cancellationToken)
    {
        return _dbSet.AddAsync(entity, cancellationToken).AsTask();
    }

    public void Update(VideoTask entity)
    {
        _dbSet.Update(entity);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<VideoTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbSet.FindAsync(new object?[] { id }, cancellationToken);
    }

    public async Task<VideoTask?> GetByCorrelationIdAsync(Guid correlationId, CancellationToken cancellationToken)
    {
        return await _dbSet.AsNoTracking().FirstOrDefaultAsync(t => t.CorrelationId == correlationId, cancellationToken);
    }

    public async Task<VideoTask?> GetByCorrelationIdForUpdateAsync(Guid correlationId, CancellationToken cancellationToken)
    {
        return await _dbSet.FirstOrDefaultAsync(t => t.CorrelationId == correlationId, cancellationToken);
    }

    public async Task<VideoTask?> GetByVeoTaskIdAsync(string veoTaskId, CancellationToken cancellationToken)
    {
        return await _dbSet.FirstOrDefaultAsync(t => t.VeoTaskId == veoTaskId, cancellationToken);
    }

    public async Task<IEnumerable<VideoTask>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _dbSet.AsNoTracking().Where(t => t.UserId == userId).ToListAsync(cancellationToken);
    }
}


