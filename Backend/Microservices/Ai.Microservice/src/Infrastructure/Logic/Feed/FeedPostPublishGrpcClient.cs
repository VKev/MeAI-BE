using Application.Abstractions.Feed;
using Grpc.Core;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Grpc.FeedPosts;

namespace Infrastructure.Logic.Feed;

public sealed class FeedPostPublishGrpcClient : IFeedPostPublishService
{
    private readonly FeedPostPublishService.FeedPostPublishServiceClient _client;

    public FeedPostPublishGrpcClient(FeedPostPublishService.FeedPostPublishServiceClient client)
    {
        _client = client;
    }

    public async Task<Result<FeedDirectPublishResult>> PublishAiPostToFeedAsync(
        FeedDirectPublishRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.PublishAiPostToFeedAsync(new PublishAiPostToFeedRequest
            {
                UserId = request.UserId.ToString(),
                WorkspaceId = request.WorkspaceId.ToString(),
                SourceAiPostId = request.SourceAiPostId.ToString(),
                Content = request.Content ?? string.Empty,
                ResourceIds = { request.ResourceIds.Select(id => id.ToString()) },
                MediaType = request.MediaType ?? string.Empty
            }, cancellationToken: cancellationToken);

            var feedPostId = Guid.TryParse(response.FeedPostId, out var parsedFeedPostId)
                ? parsedFeedPostId
                : Guid.Empty;
            var createdAt = DateTime.TryParse(response.CreatedAt, out var parsedCreatedAt)
                ? (DateTime?)parsedCreatedAt
                : null;

            return Result.Success(new FeedDirectPublishResult(feedPostId, createdAt));
        }
        catch (RpcException ex)
        {
            return Result.Failure<FeedDirectPublishResult>(new Error(MapErrorCode(ex.StatusCode), ex.Status.Detail));
        }
    }

    public async Task<Result<FeedPostModerationContent>> GetFeedPostForModerationAsync(
        Guid postId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.GetFeedPostForModerationAsync(new GetFeedPostForModerationRequest
            {
                PostId = postId.ToString(),
                UserId = userId.ToString()
            }, cancellationToken: cancellationToken);

            var parsedPostId = Guid.TryParse(response.PostId, out var id) ? id : postId;
            return Result.Success(new FeedPostModerationContent(parsedPostId, response.Content));
        }
        catch (RpcException ex)
        {
            return Result.Failure<FeedPostModerationContent>(new Error(MapErrorCode(ex.StatusCode), ex.Status.Detail));
        }
    }

    private static string MapErrorCode(StatusCode statusCode)
    {
        return statusCode switch
        {
            StatusCode.AlreadyExists => "Post.AlreadyPublishedToFeed",
            StatusCode.InvalidArgument => "FeedPublish.InvalidArgument",
            StatusCode.NotFound => "Feed.Post.NotFound",
            StatusCode.PermissionDenied => "Feed.Post.Unauthorized",
            _ => "FeedPublish.GrpcError"
        };
    }
}
