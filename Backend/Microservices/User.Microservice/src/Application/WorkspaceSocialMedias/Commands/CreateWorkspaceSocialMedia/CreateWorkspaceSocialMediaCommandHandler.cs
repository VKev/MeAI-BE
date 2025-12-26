using Application.SocialMedias;
using Application.SocialMedias.Contracts;
using Domain.Entities;
using Domain.Repositories;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.WorkspaceSocialMedias.Commands.CreateWorkspaceSocialMedia;

internal sealed class CreateWorkspaceSocialMediaCommandHandler(
    IWorkspaceRepository workspaceRepository,
    ISocialMediaRepository socialMediaRepository,
    IWorkspaceSocialMediaRepository workspaceSocialMediaRepository)
    : ICommandHandler<CreateWorkspaceSocialMediaCommand, SocialMediaResponse>
{
    public async Task<Result<SocialMediaResponse>> Handle(CreateWorkspaceSocialMediaCommand request,
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

        var socialMedia = new SocialMedia
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            Type = request.Type.Trim(),
            Metadata = request.Metadata,
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await socialMediaRepository.AddAsync(socialMedia, cancellationToken);

        var link = new WorkspaceSocialMedia
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            WorkspaceId = request.WorkspaceId,
            SocialMediaId = socialMedia.Id,
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await workspaceSocialMediaRepository.AddAsync(link, cancellationToken);

        return Result.Success(SocialMediaMapping.ToResponse(socialMedia));
    }
}
