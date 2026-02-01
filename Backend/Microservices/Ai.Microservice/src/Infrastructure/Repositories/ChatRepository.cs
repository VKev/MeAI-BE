using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class ChatRepository : IChatRepository
{
    private readonly MyDbContext _dbContext;
    private readonly DbSet<Chat> _dbSet;

    public ChatRepository(MyDbContext dbContext)
    {
        _dbContext = dbContext;
        _dbSet = dbContext.Set<Chat>();
    }

    public Task AddAsync(Chat entity, CancellationToken cancellationToken)
    {
        return _dbSet.AddAsync(entity, cancellationToken).AsTask();
    }

    public void Update(Chat entity)
    {
        _dbSet.Update(entity);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<Chat?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbSet.AsNoTracking().FirstOrDefaultAsync(chat => chat.Id == id, cancellationToken);
    }

    public async Task<Chat?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbSet.FirstOrDefaultAsync(chat => chat.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Chat>> GetBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        return await _dbSet.AsNoTracking()
            .Where(chat => chat.SessionId == sessionId)
            .ToListAsync(cancellationToken);
    }
}
