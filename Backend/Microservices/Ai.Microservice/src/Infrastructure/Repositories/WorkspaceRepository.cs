using Application.Abstractions.Workspaces;
using Domain.Repositories;

namespace Infrastructure.Repositories;

public sealed class WorkspaceRepository : IWorkspaceRepository
{
    private readonly IUserWorkspaceService _userWorkspaceService;

    public WorkspaceRepository(IUserWorkspaceService userWorkspaceService)
    {
        _userWorkspaceService = userWorkspaceService;
    }

    public async Task<bool> ExistsForUserAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken)
    {
        var remoteWorkspaceResult = await _userWorkspaceService.GetWorkspaceAsync(
            userId,
            workspaceId,
            cancellationToken);

        if (remoteWorkspaceResult.IsFailure)
        {
            throw new InvalidOperationException(remoteWorkspaceResult.Error.Description);
        }

        return remoteWorkspaceResult.Value is not null;
    }
}
