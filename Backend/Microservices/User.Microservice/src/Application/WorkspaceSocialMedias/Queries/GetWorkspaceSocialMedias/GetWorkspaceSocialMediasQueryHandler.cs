using Application.SocialMedias;
using Application.SocialMedias.Contracts;
using Domain.Repositories;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;
using System.Linq;

namespace Application.WorkspaceSocialMedias.Queries.GetWorkspaceSocialMedias;

internal sealed class GetWorkspaceSocialMediasQueryHandler(
    IWorkspaceRepository workspaceRepository,
    IWorkspaceSocialMediaRepository workspaceSocialMediaRepository)
    : IQueryHandler<GetWorkspaceSocialMediasQuery, IReadOnlyList<SocialMediaResponse>>
{
    public async Task<Result<IReadOnlyList<SocialMediaResponse>>> Handle(GetWorkspaceSocialMediasQuery request,
        CancellationToken cancellationToken)
    {
        var workspace = await workspaceRepository.GetByIdForUserAsync(
            request.WorkspaceId,
            request.UserId,
            cancellationToken);

        if (workspace == null)
        {
            return Result.Failure<IReadOnlyList<SocialMediaResponse>>(
                new Error("Workspace.NotFound", "Workspace not found"));
        }

        var socialMedias = await workspaceSocialMediaRepository.GetSocialMediasForWorkspaceAsync(
            request.WorkspaceId,
            request.UserId,
            request.CursorCreatedAt,
            request.CursorId,
            request.Limit,
            cancellationToken);

        var response = socialMedias.Select(SocialMediaMapping.ToResponse).ToList();
        return Result.Success<IReadOnlyList<SocialMediaResponse>>(response);
    }
}
