using Application.Workspaces.Contracts;
using Domain.Repositories;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Workspaces.Commands.UpdateWorkspace;

internal sealed class UpdateWorkspaceCommandHandler(IWorkspaceRepository workspaceRepository)
    : ICommandHandler<UpdateWorkspaceCommand, WorkspaceResponse>
{
    public async Task<Result<WorkspaceResponse>> Handle(UpdateWorkspaceCommand request,
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

        workspace.Name = request.Name.Trim();
        workspace.Type = string.IsNullOrWhiteSpace(request.Type) ? null : request.Type.Trim();
        workspace.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        workspace.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;

        workspaceRepository.Update(workspace);

        return Result.Success(WorkspaceMapping.ToResponse(workspace));
    }
}
