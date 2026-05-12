using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Feed;

public interface IFeedPostPublishService
{
    Task<Result<FeedDirectPublishResult>> PublishAiPostToFeedAsync(
        FeedDirectPublishRequest request,
        CancellationToken cancellationToken);

    Task<Result<FeedPostModerationContent>> GetFeedPostForModerationAsync(
        Guid postId,
        Guid userId,
        CancellationToken cancellationToken);
}

public sealed record FeedDirectPublishRequest(
    Guid UserId,
    Guid WorkspaceId,
    Guid SourceAiPostId,
    string? Content,
    IReadOnlyList<Guid> ResourceIds,
    string? MediaType);

public sealed record FeedDirectPublishResult(
    Guid FeedPostId,
    DateTime? CreatedAt);

public sealed record FeedPostModerationContent(
    Guid PostId,
    string? Content);
