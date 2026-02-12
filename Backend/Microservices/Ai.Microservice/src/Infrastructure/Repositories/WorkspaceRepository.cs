using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class WorkspaceRepository : IWorkspaceRepository
{
    private readonly DbSet<Workspace> _dbSet;

    public WorkspaceRepository(MyDbContext dbContext)
    {
        _dbSet = dbContext.Set<Workspace>();
    }

    public async Task<bool> ExistsForUserAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken)
    {
        return await _dbSet.AsNoTracking()
            .AnyAsync(
                workspace => workspace.Id == workspaceId && workspace.UserId == userId && workspace.DeletedAt == null,
                cancellationToken);
    }
}
