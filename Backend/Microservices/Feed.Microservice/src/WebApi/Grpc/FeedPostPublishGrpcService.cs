using Application.Posts.Commands;
using Domain.Entities;
using Grpc.Core;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Application.Abstractions.Data;
using SharedLibrary.Grpc.FeedPosts;

namespace WebApi.Grpc;

public sealed class FeedPostPublishGrpcService : FeedPostPublishService.FeedPostPublishServiceBase
{
    private readonly IMediator _mediator;
    private readonly IUnitOfWork _unitOfWork;

    public FeedPostPublishGrpcService(IMediator mediator, IUnitOfWork unitOfWork)
    {
        _mediator = mediator;
        _unitOfWork = unitOfWork;
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

    public override async Task<GetFeedPostForModerationResponse> GetFeedPostForModeration(
        GetFeedPostForModerationRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.PostId, out var postId) || postId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid postId."));
        }

        if (!Guid.TryParse(request.UserId, out var userId) || userId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid userId."));
        }

        var post = await _unitOfWork.Repository<Post>()
            .GetAll()
            .Where(item => item.Id == postId && !item.IsDeleted && item.DeletedAt == null)
            .Select(item => new { item.Id, item.UserId, item.Content })
            .FirstOrDefaultAsync(context.CancellationToken);

        if (post is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Feed post not found."));
        }

        if (post.UserId != userId)
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "User does not own this feed post."));
        }

        return new GetFeedPostForModerationResponse
        {
            PostId = post.Id.ToString(),
            Content = post.Content ?? string.Empty
        };
    }

    public override async Task<UnpublishAiPostFromFeedResponse> UnpublishAiPostFromFeed(
        UnpublishAiPostFromFeedRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId) || userId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid userId."));
        }

        if (!Guid.TryParse(request.FeedPostId, out var feedPostId) || feedPostId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid feedPostId."));
        }

        var result = await _mediator.Send(
            new DeletePostCommand(userId, feedPostId, SkipAiMirrorDelete: true),
            context.CancellationToken);

        if (result.IsFailure)
        {
            throw new RpcException(new Status(MapStatusCode(result.Error.Code), result.Error.Description));
        }

        return new UnpublishAiPostFromFeedResponse
        {
            Unpublished = result.Value
        };
    }

    public override async Task<UpdateAiPostOnFeedResponse> UpdateAiPostOnFeed(
        UpdateAiPostOnFeedRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId) || userId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid userId."));
        }

        if (!Guid.TryParse(request.FeedPostId, out var feedPostId) || feedPostId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid feedPostId."));
        }

        var post = await _unitOfWork.Repository<Post>()
            .GetAll()
            .Where(item => item.Id == feedPostId && !item.IsDeleted && item.DeletedAt == null)
            .Select(item => new { item.UserId, item.ResourceIds, item.MediaType })
            .FirstOrDefaultAsync(context.CancellationToken);

        if (post is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Feed post not found."));
        }

        if (post.UserId != userId)
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "User does not own this feed post."));
        }

        var result = await _mediator.Send(
            new UpdatePostCommand(
                userId,
                feedPostId,
                NormalizeString(request.Content),
                post.ResourceIds,
                post.MediaType),
            context.CancellationToken);

        if (result.IsFailure)
        {
            throw new RpcException(new Status(MapStatusCode(result.Error.Code), result.Error.Description));
        }

        return new UpdateAiPostOnFeedResponse
        {
            Updated = true
        };
    }

    private static StatusCode MapStatusCode(string? errorCode)
    {
        return errorCode switch
        {
            "Feed.Post.AlreadyPublishedToFeed" => StatusCode.AlreadyExists,
            "Feed.Post.NotFound" => StatusCode.NotFound,
            "Feed.Forbidden" => StatusCode.PermissionDenied,
            _ => StatusCode.InvalidArgument
        };
    }

    private static string? NormalizeString(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
