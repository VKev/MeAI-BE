using Application.SocialMedias.Models;
using Application.SocialMedias.Queries;
using Application.WorkspaceSocialMedias.Queries;
using Grpc.Core;
using MediatR;
using SharedLibrary.Grpc.UserResources;

namespace WebApi.Grpc;

public sealed class UserSocialMediaGrpcService : UserSocialMediaService.UserSocialMediaServiceBase
{
    private readonly IMediator _mediator;

    public UserSocialMediaGrpcService(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override async Task<GetSocialMediasByIdsResponse> GetSocialMediasByIds(
        GetSocialMediasByIdsRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid userId."));
        }

        var socialMediaIds = new List<Guid>();
        foreach (var socialMediaId in request.SocialMediaIds)
        {
            if (!Guid.TryParse(socialMediaId, out var parsedId))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid social_media_id."));
            }

            socialMediaIds.Add(parsedId);
        }

        var result = await _mediator.Send(
            new GetSocialMediasByIdsQuery(userId, socialMediaIds),
            context.CancellationToken);

        if (result.IsFailure)
        {
            throw new RpcException(new Status(StatusCode.NotFound, result.Error.Description));
        }

        var response = new GetSocialMediasByIdsResponse();
        response.SocialMedias.AddRange(result.Value.Select(item => new SocialMediaRecord
        {
            SocialMediaId = item.Id.ToString(),
            Type = item.Type ?? string.Empty,
            MetadataJson = item.MetadataJson ?? string.Empty
        }));

        return response;
    }

    public override async Task<GetSocialMediasByUserResponse> GetSocialMediasByUser(
        GetSocialMediasByUserRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid userId."));
        }

        var result = await _mediator.Send(
            new GetSocialMediasQuery(userId, null, null, 100),
            context.CancellationToken);

        if (result.IsFailure)
        {
            throw new RpcException(new Status(StatusCode.NotFound, result.Error.Description));
        }

        var response = new GetSocialMediasByUserResponse();
        var filtered = string.IsNullOrWhiteSpace(request.Platform)
            ? result.Value
            : result.Value.Where(item => string.Equals(item.Type, request.Platform, StringComparison.OrdinalIgnoreCase));

        response.SocialMedias.AddRange(filtered.Select(MapSummary));
        return response;
    }

    public override async Task<GetSocialMediasByUserResponse> GetWorkspaceSocialMedias(
        GetWorkspaceSocialMediasRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid userId."));
        }

        if (!Guid.TryParse(request.WorkspaceId, out var workspaceId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid workspaceId."));
        }

        var result = await _mediator.Send(
            new GetWorkspaceSocialMediasQuery(workspaceId, userId, null, null, 100),
            context.CancellationToken);

        if (result.IsFailure)
        {
            throw new RpcException(new Status(StatusCode.NotFound, result.Error.Description));
        }

        var response = new GetSocialMediasByUserResponse();
        response.SocialMedias.AddRange(result.Value.Select(MapSummary));
        return response;
    }

    private static SocialMediaSummaryRecord MapSummary(SocialMediaResponse item)
    {
        return new SocialMediaSummaryRecord
        {
            SocialMediaId = item.Id.ToString(),
            Type = item.Type ?? string.Empty,
            Username = item.Profile?.Username ?? string.Empty,
            DisplayName = item.Profile?.DisplayName ?? string.Empty,
            ProfilePictureUrl = item.Profile?.ProfilePictureUrl ?? string.Empty,
            PageId = item.Profile?.PageId ?? string.Empty,
            PageName = item.Profile?.PageName ?? string.Empty,
            CreatedAt = item.CreatedAt?.ToString("O") ?? string.Empty,
            UpdatedAt = item.UpdatedAt?.ToString("O") ?? string.Empty
        };
    }
}
