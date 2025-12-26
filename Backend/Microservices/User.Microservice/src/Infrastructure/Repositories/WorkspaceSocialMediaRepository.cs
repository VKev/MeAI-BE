using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class WorkspaceSocialMediaRepository(MyDbContext context) : IWorkspaceSocialMediaRepository
{
    public async Task<IReadOnlyList<SocialMedia>> GetSocialMediasForWorkspaceAsync(Guid workspaceId, Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await context.Set<WorkspaceSocialMedia>()
            .Where(link => link.WorkspaceId == workspaceId && link.UserId == userId && link.DeletedAt == null)
            .Join(context.Set<SocialMedia>(),
                link => link.SocialMediaId,
                social => social.Id,
                (_, social) => social)
            .Where(social => social.DeletedAt == null && social.UserId == userId)
            .OrderBy(social => social.CreatedAt)
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
                        link.DeletedAt == null,
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
