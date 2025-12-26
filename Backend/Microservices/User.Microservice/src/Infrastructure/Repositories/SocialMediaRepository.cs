using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class SocialMediaRepository(MyDbContext context) : ISocialMediaRepository
{
    public async Task<IReadOnlyList<SocialMedia>> GetForUserAsync(Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await context.Set<SocialMedia>()
            .Where(sm => sm.UserId == userId && sm.DeletedAt == null)
            .OrderBy(sm => sm.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<SocialMedia?> GetByIdForUserAsync(Guid id, Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await context.Set<SocialMedia>()
            .FirstOrDefaultAsync(
                sm => sm.Id == id && sm.UserId == userId && sm.DeletedAt == null,
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
