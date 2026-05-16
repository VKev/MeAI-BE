using Application.Abstractions.Feed;
using Application.Abstractions.Gemini;
using Application.Posts;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Posts.Commands;

public sealed record CheckPostSensitiveContentCommand(
    Guid PostId,
    Guid UserId) : IRequest<Result<CheckSensitiveContentResponse>>;

public sealed record CheckSensitiveContentResponse(
    Guid PostId,
    bool IsSensitive,
    string? Category,
    string? Reason,
    double ConfidenceScore);

public sealed class CheckPostSensitiveContentCommandHandler
    : IRequestHandler<CheckPostSensitiveContentCommand, Result<CheckSensitiveContentResponse>>
{
    private readonly IPostRepository _postRepository;
    private readonly IFeedPostPublishService _feedPostPublishService;
    private readonly IGeminiContentModerationService _contentModerationService;

    public CheckPostSensitiveContentCommandHandler(
        IPostRepository postRepository,
        IFeedPostPublishService feedPostPublishService,
        IGeminiContentModerationService contentModerationService)
    {
        _postRepository = postRepository;
        _feedPostPublishService = feedPostPublishService;
        _contentModerationService = contentModerationService;
    }

    public async Task<Result<CheckSensitiveContentResponse>> Handle(
        CheckPostSensitiveContentCommand request,
        CancellationToken cancellationToken)
    {
        var post = await _postRepository.GetByIdAsync(request.PostId, cancellationToken);
        var moderatedPostId = request.PostId;
        string text;

        if (post is not null && !post.DeletedAt.HasValue)
        {
            if (post.UserId != request.UserId)
            {
                return Result.Failure<CheckSensitiveContentResponse>(PostErrors.Unauthorized);
            }

            text = post.Content?.Content?.Trim() ?? string.Empty;
        }
        else
        {
            var feedPostResult = await _feedPostPublishService.GetFeedPostForModerationAsync(
                request.PostId,
                request.UserId,
                cancellationToken);

            if (feedPostResult.IsFailure)
            {
                return Result.Failure<CheckSensitiveContentResponse>(MapFeedPostError(feedPostResult.Error));
            }

            moderatedPostId = feedPostResult.Value.PostId;
            text = feedPostResult.Value.Content?.Trim() ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return Result.Success(new CheckSensitiveContentResponse(
                moderatedPostId,
                IsSensitive: false,
                Category: null,
                Reason: "Post has no text content to analyze.",
                ConfidenceScore: 0.0));
        }

        var moderationResult = await _contentModerationService.CheckSensitiveContentAsync(
            new ContentModerationRequest(text),
            cancellationToken);

        if (moderationResult.IsFailure)
        {
            return Result.Failure<CheckSensitiveContentResponse>(moderationResult.Error);
        }

        var result = moderationResult.Value;
        return Result.Success(new CheckSensitiveContentResponse(
            moderatedPostId,
            result.IsSensitive,
            result.Category,
            result.Reason,
            result.ConfidenceScore));
    }

    private static Error MapFeedPostError(Error error)
    {
        return error.Code switch
        {
            "Feed.Post.NotFound" => PostErrors.NotFound,
            "Feed.Post.Unauthorized" => PostErrors.Unauthorized,
            _ => error
        };
    }
}
