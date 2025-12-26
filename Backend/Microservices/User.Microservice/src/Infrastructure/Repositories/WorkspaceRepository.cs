using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class WorkspaceRepository(MyDbContext context) : IWorkspaceRepository
{
    public async Task<IReadOnlyList<Workspace>> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await context.Set<Workspace>()
            .Where(w => w.UserId == userId && w.DeletedAt == null)
            .OrderBy(w => w.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<Workspace?> GetByIdForUserAsync(Guid id, Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await context.Set<Workspace>()
            .FirstOrDefaultAsync(w => w.Id == id && w.UserId == userId && w.DeletedAt == null, cancellationToken);
    }

    public Task AddAsync(Workspace workspace, CancellationToken cancellationToken = default)
    {
        return context.Set<Workspace>().AddAsync(workspace, cancellationToken).AsTask();
    }

    public void Update(Workspace workspace)
    {
        context.Set<Workspace>().Update(workspace);
    }
}
