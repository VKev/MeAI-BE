using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class ChatSessionRepository : IChatSessionRepository
{
    private readonly MyDbContext _dbContext;
    private readonly DbSet<ChatSession> _dbSet;

    public ChatSessionRepository(MyDbContext dbContext)
    {
        _dbContext = dbContext;
        _dbSet = dbContext.Set<ChatSession>();
    }

    public Task AddAsync(ChatSession entity, CancellationToken cancellationToken)
    {
        return _dbSet.AddAsync(entity, cancellationToken).AsTask();
    }

    public void Update(ChatSession entity)
    {
        _dbSet.Update(entity);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ChatSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbSet.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<ChatSession?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbSet.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<ChatSession>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _dbSet.AsNoTracking()
            .Where(s => s.UserId == userId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<ChatSession>> GetByWorkspaceIdAsync(
        Guid userId,
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        return await _dbSet.AsNoTracking()
            .Where(s => s.UserId == userId && s.WorkspaceId == workspaceId)
            .ToListAsync(cancellationToken);
    }
}
