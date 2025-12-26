using Domain.Entities;

namespace Domain.Repositories;

public interface IWorkspaceSocialMediaRepository
{
    Task<IReadOnlyList<SocialMedia>> GetSocialMediasForWorkspaceAsync(Guid workspaceId, Guid userId,
        CancellationToken cancellationToken = default);
    Task<WorkspaceSocialMedia?> GetLinkAsync(Guid workspaceId, Guid socialMediaId, Guid userId,
        CancellationToken cancellationToken = default);
    Task AddAsync(WorkspaceSocialMedia link, CancellationToken cancellationToken = default);
    void Update(WorkspaceSocialMedia link);
}
