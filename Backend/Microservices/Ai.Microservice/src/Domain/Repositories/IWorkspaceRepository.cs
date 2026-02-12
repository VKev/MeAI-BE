namespace Domain.Repositories;

public interface IWorkspaceRepository
{
    Task<bool> ExistsForUserAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken);
}
