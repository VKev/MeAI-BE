using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class WorkspaceRepository(MyDbContext context) : IWorkspaceRepository
{
    public async Task<IReadOnlyList<Workspace>> GetForUserAsync(Guid userId, DateTime? cursorCreatedAt, Guid? cursorId,
        int? limit, CancellationToken cancellationToken = default)
    {
        const int defaultPageSize = 50;
        const int maxPageSize = 100;
        var pageSize = Math.Clamp(limit ?? defaultPageSize, 1, maxPageSize);

        var query = context.Set<Workspace>()
            .Where(w => w.UserId == userId && !w.IsDeleted)
            .AsQueryable();

        if (cursorCreatedAt.HasValue && cursorId.HasValue)
        {
            var createdAt = cursorCreatedAt.Value;
            var lastId = cursorId.Value;
            query = query.Where(w =>
                EF.Functions.LessThan(
                    ValueTuple.Create(w.CreatedAt, w.Id),
                    ValueTuple.Create(createdAt, lastId)));
        }

        return await query
            .OrderByDescending(w => w.CreatedAt)
            .ThenByDescending(w => w.Id)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<Workspace?> GetByIdForUserAsync(Guid id, Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await context.Set<Workspace>()
            .FirstOrDefaultAsync(w => w.Id == id && w.UserId == userId && !w.IsDeleted, cancellationToken);
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
