using Application.Workspaces.Contracts;
using Domain.Repositories;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Workspaces.Queries.GetWorkspaceById;

internal sealed class GetWorkspaceByIdQueryHandler(IWorkspaceRepository workspaceRepository)
    : IQueryHandler<GetWorkspaceByIdQuery, WorkspaceResponse>
{
    public async Task<Result<WorkspaceResponse>> Handle(GetWorkspaceByIdQuery request,
        CancellationToken cancellationToken)
    {
        var workspace = await workspaceRepository.GetByIdForUserAsync(
            request.WorkspaceId,
            request.UserId,
            cancellationToken);

        if (workspace == null)
        {
            return Result.Failure<WorkspaceResponse>(new Error("Workspace.NotFound", "Workspace not found"));
        }

        return Result.Success(WorkspaceMapping.ToResponse(workspace));
    }
}
