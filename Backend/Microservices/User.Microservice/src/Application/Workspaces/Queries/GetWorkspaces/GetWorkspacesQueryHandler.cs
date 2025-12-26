using Application.Workspaces.Contracts;
using System.Linq;
using Domain.Repositories;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Workspaces.Queries.GetWorkspaces;

internal sealed class GetWorkspacesQueryHandler(IWorkspaceRepository workspaceRepository)
    : IQueryHandler<GetWorkspacesQuery, IReadOnlyList<WorkspaceResponse>>
{
    public async Task<Result<IReadOnlyList<WorkspaceResponse>>> Handle(GetWorkspacesQuery request,
        CancellationToken cancellationToken)
    {
        var workspaces = await workspaceRepository.GetForUserAsync(request.UserId, cancellationToken);
        var response = workspaces.Select(WorkspaceMapping.ToResponse).ToList();
        return Result.Success<IReadOnlyList<WorkspaceResponse>>(response);
    }
}
