using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Extensions;

namespace Infrastructure.Repositories;

public sealed class WorkspaceRepository : IWorkspaceRepository
{
    private readonly MyDbContext _dbContext;
    private readonly DbSet<Workspace> _dbSet;

    public WorkspaceRepository(MyDbContext dbContext)
    {
        _dbContext = dbContext;
        _dbSet = dbContext.Set<Workspace>();
    }

    public async Task<bool> ExistsForUserAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken)
    {
        return await _dbSet.AsNoTracking()
            .AnyAsync(
                workspace => workspace.Id == workspaceId && workspace.UserId == userId && workspace.DeletedAt == null,
                cancellationToken);
    }

    public async Task EnsureExistsForUserAsync(
        Guid workspaceId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var existingWorkspace = await _dbSet
            .FirstOrDefaultAsync(
                workspace => workspace.Id == workspaceId && workspace.UserId == userId,
                cancellationToken);

        if (existingWorkspace is not null)
        {
            if (existingWorkspace.DeletedAt.HasValue)
            {
                existingWorkspace.DeletedAt = null;
                existingWorkspace.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return;
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var workspace = new Workspace
        {
            Id = workspaceId,
            UserId = userId,
            WorkspaceName = $"Workspace-{workspaceId:N}",
            WorkspaceType = null,
            Description = null,
            CreatedAt = now,
            UpdatedAt = null,
            DeletedAt = null
        };

        await _dbSet.AddAsync(workspace, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
