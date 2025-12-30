using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class SocialMediaRepository(MyDbContext context) : ISocialMediaRepository
{
    public async Task<IReadOnlyList<SocialMedia>> GetForUserAsync(Guid userId, DateTime? cursorCreatedAt, Guid? cursorId,
        int? limit, CancellationToken cancellationToken = default)
    {
        const int defaultPageSize = 50;
        const int maxPageSize = 100;
        var pageSize = Math.Clamp(limit ?? defaultPageSize, 1, maxPageSize);

        var query = context.Set<SocialMedia>()
            .Where(sm => sm.UserId == userId && !sm.IsDeleted)
            .AsQueryable();

        if (cursorCreatedAt.HasValue && cursorId.HasValue)
        {
            var createdAt = cursorCreatedAt.Value;
            var lastId = cursorId.Value;
            query = query.Where(sm =>
                EF.Functions.LessThan(
                    ValueTuple.Create(sm.CreatedAt, sm.Id),
                    ValueTuple.Create(createdAt, lastId)));
        }

        return await query
            .OrderByDescending(sm => sm.CreatedAt)
            .ThenByDescending(sm => sm.Id)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<SocialMedia?> GetByIdForUserAsync(Guid id, Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await context.Set<SocialMedia>()
            .FirstOrDefaultAsync(
                sm => sm.Id == id && sm.UserId == userId && !sm.IsDeleted,
                cancellationToken);
    }

    public Task AddAsync(SocialMedia socialMedia, CancellationToken cancellationToken = default)
    {
        return context.Set<SocialMedia>().AddAsync(socialMedia, cancellationToken).AsTask();
    }

    public void Update(SocialMedia socialMedia)
    {
        context.Set<SocialMedia>().Update(socialMedia);
    }
}
