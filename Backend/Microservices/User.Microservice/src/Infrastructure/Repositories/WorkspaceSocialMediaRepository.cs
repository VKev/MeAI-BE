using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class WorkspaceSocialMediaRepository(MyDbContext context) : IWorkspaceSocialMediaRepository
{
    public async Task<IReadOnlyList<SocialMedia>> GetSocialMediasForWorkspaceAsync(Guid workspaceId, Guid userId,
        DateTime? cursorCreatedAt, Guid? cursorId, int? limit, CancellationToken cancellationToken = default)
    {
        const int defaultPageSize = 50;
        const int maxPageSize = 100;
        var pageSize = Math.Clamp(limit ?? defaultPageSize, 1, maxPageSize);

        var query = context.Set<WorkspaceSocialMedia>()
            .Where(link => link.WorkspaceId == workspaceId && link.UserId == userId && !link.IsDeleted)
            .Join(context.Set<SocialMedia>(),
                link => link.SocialMediaId,
                social => social.Id,
                (_, social) => social)
            .Where(social => !social.IsDeleted && social.UserId == userId)
            .AsQueryable();

        if (cursorCreatedAt.HasValue && cursorId.HasValue)
        {
            var createdAt = cursorCreatedAt.Value;
            var lastId = cursorId.Value;
            query = query.Where(social =>
                EF.Functions.LessThanOrEqual(
                    ValueTuple.Create(social.CreatedAt, social.Id),
                    ValueTuple.Create(createdAt, lastId)));
        }

        return await query
            .OrderByDescending(social => social.CreatedAt)
            .ThenBy(social => social.Id)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<WorkspaceSocialMedia?> GetLinkAsync(Guid workspaceId, Guid socialMediaId, Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await context.Set<WorkspaceSocialMedia>()
            .FirstOrDefaultAsync(
                link => link.WorkspaceId == workspaceId &&
                        link.SocialMediaId == socialMediaId &&
                        link.UserId == userId &&
                        !link.IsDeleted,
                cancellationToken);
    }

    public Task AddAsync(WorkspaceSocialMedia link, CancellationToken cancellationToken = default)
    {
        return context.Set<WorkspaceSocialMedia>().AddAsync(link, cancellationToken).AsTask();
    }

    public void Update(WorkspaceSocialMedia link)
    {
        context.Set<WorkspaceSocialMedia>().Update(link);
    }
}
