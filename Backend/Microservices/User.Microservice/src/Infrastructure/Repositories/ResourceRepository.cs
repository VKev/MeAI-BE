using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class ResourceRepository(MyDbContext context) : IResourceRepository
{
    public async Task<IReadOnlyList<Resource>> GetForUserAsync(Guid userId, DateTime? cursorCreatedAt, Guid? cursorId,
        int? limit, CancellationToken cancellationToken = default)
    {
        const int defaultPageSize = 50;
        const int maxPageSize = 100;
        var pageSize = Math.Clamp(limit ?? defaultPageSize, 1, maxPageSize);

        var query = context.Set<Resource>()
            .Where(resource => resource.UserId == userId && !resource.IsDeleted)
            .AsQueryable();

        if (cursorCreatedAt.HasValue && cursorId.HasValue)
        {
            var createdAt = cursorCreatedAt.Value;
            var lastId = cursorId.Value;
            query = query.Where(resource =>
                EF.Functions.LessThan(
                    ValueTuple.Create(resource.CreatedAt, resource.Id),
                    ValueTuple.Create(createdAt, lastId)));
        }

        return await query
            .OrderByDescending(resource => resource.CreatedAt)
            .ThenByDescending(resource => resource.Id)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<Resource?> GetByIdForUserAsync(Guid id, Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await context.Set<Resource>()
            .FirstOrDefaultAsync(
                resource => resource.Id == id && resource.UserId == userId && !resource.IsDeleted,
                cancellationToken);
    }

    public Task AddAsync(Resource resource, CancellationToken cancellationToken = default)
    {
        return context.Set<Resource>().AddAsync(resource, cancellationToken).AsTask();
    }

    public void Update(Resource resource)
    {
        context.Set<Resource>().Update(resource);
    }
}
