using Application.SocialMedias;
using Application.SocialMedias.Contracts;
using Domain.Repositories;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.WorkspaceSocialMedias.Commands.UpdateWorkspaceSocialMedia;

internal sealed class UpdateWorkspaceSocialMediaCommandHandler(
    IWorkspaceRepository workspaceRepository,
    ISocialMediaRepository socialMediaRepository,
    IWorkspaceSocialMediaRepository workspaceSocialMediaRepository)
    : ICommandHandler<UpdateWorkspaceSocialMediaCommand, SocialMediaResponse>
{
    public async Task<Result<SocialMediaResponse>> Handle(UpdateWorkspaceSocialMediaCommand request,
        CancellationToken cancellationToken)
    {
        var workspace = await workspaceRepository.GetByIdForUserAsync(
            request.WorkspaceId,
            request.UserId,
            cancellationToken);

        if (workspace == null)
        {
            return Result.Failure<SocialMediaResponse>(new Error("Workspace.NotFound", "Workspace not found"));
        }

        var link = await workspaceSocialMediaRepository.GetLinkAsync(
            request.WorkspaceId,
            request.SocialMediaId,
            request.UserId,
            cancellationToken);

        if (link == null)
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("WorkspaceSocialMedia.NotFound", "Social media not found in workspace"));
        }

        var socialMedia = await socialMediaRepository.GetByIdForUserAsync(
            request.SocialMediaId,
            request.UserId,
            cancellationToken);

        if (socialMedia == null)
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("SocialMedia.NotFound", "Social media not found"));
        }

        socialMedia.Type = request.Type.Trim();
        socialMedia.Metadata = request.Metadata;
        socialMedia.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        socialMediaRepository.Update(socialMedia);

        link.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        workspaceSocialMediaRepository.Update(link);

        return Result.Success(SocialMediaMapping.ToResponse(socialMedia));
    }
}
