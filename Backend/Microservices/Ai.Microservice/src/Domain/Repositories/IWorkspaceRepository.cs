namespace Domain.Repositories;

public interface IWorkspaceRepository
{
    Task<bool> ExistsForUserAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken);
    Task EnsureExistsForUserAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken);
}
