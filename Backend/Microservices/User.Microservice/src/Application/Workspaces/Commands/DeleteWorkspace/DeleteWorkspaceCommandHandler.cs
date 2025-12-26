using Domain.Repositories;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Workspaces.Commands.DeleteWorkspace;

internal sealed class DeleteWorkspaceCommandHandler(IWorkspaceRepository workspaceRepository)
    : ICommandHandler<DeleteWorkspaceCommand>
{
    public async Task<Result> Handle(DeleteWorkspaceCommand request, CancellationToken cancellationToken)
    {
        var workspace = await workspaceRepository.GetByIdForUserAsync(
            request.WorkspaceId,
            request.UserId,
            cancellationToken);

        if (workspace == null)
        {
            return Result.Failure(new Error("Workspace.NotFound", "Workspace not found"));
        }

        workspace.DeletedAt = DateTimeExtensions.PostgreSqlUtcNow;
        workspace.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        workspaceRepository.Update(workspace);

        return Result.Success();
    }
}
