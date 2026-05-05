using Application.Posts.Commands;
using Grpc.Core;
using MediatR;
using SharedLibrary.Grpc.FeedPosts;

namespace WebApi.Grpc;

public sealed class FeedPostPublishGrpcService : FeedPostPublishService.FeedPostPublishServiceBase
{
    private readonly IMediator _mediator;

    public FeedPostPublishGrpcService(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override async Task<PublishAiPostToFeedResponse> PublishAiPostToFeed(
        PublishAiPostToFeedRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId) || userId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid userId."));
        }

        if (!Guid.TryParse(request.WorkspaceId, out var workspaceId) || workspaceId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid workspaceId."));
        }

        if (!Guid.TryParse(request.SourceAiPostId, out var sourceAiPostId) || sourceAiPostId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid sourceAiPostId."));
        }

        var resourceIds = new List<Guid>();
        foreach (var resourceId in request.ResourceIds)
        {
            if (!Guid.TryParse(resourceId, out var parsedResourceId) || parsedResourceId == Guid.Empty)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid resourceIds entry."));
            }

            resourceIds.Add(parsedResourceId);
        }

        var result = await _mediator.Send(
            new CreatePostCommand(
                UserId: userId,
                Content: NormalizeString(request.Content),
                ResourceIds: resourceIds,
                MediaType: NormalizeString(request.MediaType),
                AiPostId: sourceAiPostId,
                SkipAiMirror: true),
            context.CancellationToken);

        if (result.IsFailure)
        {
            throw new RpcException(new Status(MapStatusCode(result.Error.Code), result.Error.Description));
        }

        return new PublishAiPostToFeedResponse
        {
            FeedPostId = result.Value.Id.ToString(),
            CreatedAt = result.Value.CreatedAt?.ToString("O") ?? string.Empty
        };
    }

    private static StatusCode MapStatusCode(string? errorCode)
    {
        return string.Equals(errorCode, "Feed.Post.AlreadyPublishedToFeed", StringComparison.Ordinal)
            ? StatusCode.AlreadyExists
            : StatusCode.InvalidArgument;
    }

    private static string? NormalizeString(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
