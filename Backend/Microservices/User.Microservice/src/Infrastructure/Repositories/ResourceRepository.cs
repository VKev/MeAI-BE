using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class ResourceRepository(MyDbContext context) : IResourceRepository
{
    public async Task<IReadOnlyList<Resource>> GetForUserAsync(Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await context.Set<Resource>()
            .Where(resource => resource.UserId == userId && resource.DeletedAt == null)
            .OrderBy(resource => resource.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<Resource?> GetByIdForUserAsync(Guid id, Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await context.Set<Resource>()
            .FirstOrDefaultAsync(
                resource => resource.Id == id && resource.UserId == userId && resource.DeletedAt == null,
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
