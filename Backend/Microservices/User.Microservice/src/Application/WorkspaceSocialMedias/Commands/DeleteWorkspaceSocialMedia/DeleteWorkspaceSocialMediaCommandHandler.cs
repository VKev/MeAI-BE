using Domain.Repositories;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.WorkspaceSocialMedias.Commands.DeleteWorkspaceSocialMedia;

internal sealed class DeleteWorkspaceSocialMediaCommandHandler(
    IWorkspaceRepository workspaceRepository,
    IWorkspaceSocialMediaRepository workspaceSocialMediaRepository)
    : ICommandHandler<DeleteWorkspaceSocialMediaCommand>
{
    public async Task<Result> Handle(DeleteWorkspaceSocialMediaCommand request, CancellationToken cancellationToken)
    {
        var workspace = await workspaceRepository.GetByIdForUserAsync(
            request.WorkspaceId,
            request.UserId,
            cancellationToken);

        if (workspace == null)
        {
            return Result.Failure(new Error("Workspace.NotFound", "Workspace not found"));
        }

        var link = await workspaceSocialMediaRepository.GetLinkAsync(
            request.WorkspaceId,
            request.SocialMediaId,
            request.UserId,
            cancellationToken);

        if (link == null)
        {
            return Result.Failure(new Error("WorkspaceSocialMedia.NotFound", "Social media not found in workspace"));
        }

        link.DeletedAt = DateTimeExtensions.PostgreSqlUtcNow;
        link.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        workspaceSocialMediaRepository.Update(link);

        return Result.Success();
    }
}
