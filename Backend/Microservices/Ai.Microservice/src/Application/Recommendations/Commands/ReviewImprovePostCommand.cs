using Application.Posts;
using Application.Posts.Models;
using Domain.Entities;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Recommendations.Commands;

public sealed record ApproveImprovePostCommand(Guid UserId, Guid PostId) : IRequest<Result<PostResponse>>;

public sealed record RejectImprovePostCommand(Guid UserId, Guid PostId) : IRequest<Result<PostResponse>>;

public sealed class ApproveImprovePostCommandHandler
    : IRequestHandler<ApproveImprovePostCommand, Result<PostResponse>>
{
    private readonly IPostRepository _postRepository;
    private readonly IRecommendPostRepository _recommendPostRepository;
    private readonly PostResponseBuilder _postResponseBuilder;

    public ApproveImprovePostCommandHandler(
        IPostRepository postRepository,
        IRecommendPostRepository recommendPostRepository,
        PostResponseBuilder postResponseBuilder)
    {
        _postRepository = postRepository;
        _recommendPostRepository = recommendPostRepository;
        _postResponseBuilder = postResponseBuilder;
    }

    public async Task<Result<PostResponse>> Handle(
        ApproveImprovePostCommand request,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadPostAndSuggestionAsync(
            _postRepository,
            _recommendPostRepository,
            request.UserId,
            request.PostId,
            cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<PostResponse>(loaded.Error);
        }

        var (post, suggestion) = loaded.Value;
        if (!string.Equals(suggestion.Status, RecommendPostStatuses.Completed, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<PostResponse>(ImprovePostReviewErrors.NotCompleted);
        }

        if (suggestion.ImproveCaption && string.IsNullOrWhiteSpace(suggestion.ResultCaption))
        {
            return Result.Failure<PostResponse>(ImprovePostReviewErrors.MissingResultCaption);
        }

        if (suggestion.ImproveImage && !suggestion.ResultResourceId.HasValue)
        {
            return Result.Failure<PostResponse>(ImprovePostReviewErrors.MissingResultResource);
        }

        var existingContent = post.Content;
        post.Content = new PostContent
        {
            Content = suggestion.ImproveCaption
                ? suggestion.ResultCaption!.Trim()
                : existingContent?.Content,
            Hashtag = existingContent?.Hashtag,
            PostType = existingContent?.PostType,
            ResourceList = suggestion.ImproveImage
                ? new List<string> { suggestion.ResultResourceId!.Value.ToString() }
                : existingContent?.ResourceList is null
                    ? null
                    : new List<string>(existingContent.ResourceList)
        };

        ClearSuggestion(post, suggestion, _postRepository, _recommendPostRepository);
        await _recommendPostRepository.SaveChangesAsync(cancellationToken);

        var response = await _postResponseBuilder.BuildAsync(request.UserId, post, cancellationToken);
        return Result.Success(response);
    }

    private static async Task<Result<(Post Post, RecommendPost Suggestion)>> LoadPostAndSuggestionAsync(
        IPostRepository postRepository,
        IRecommendPostRepository recommendPostRepository,
        Guid userId,
        Guid postId,
        CancellationToken cancellationToken)
    {
        var post = await postRepository.GetByIdForUpdateAsync(postId, cancellationToken);
        if (post is null || post.DeletedAt.HasValue)
        {
            return Result.Failure<(Post, RecommendPost)>(PostErrors.NotFound);
        }

        if (post.UserId != userId)
        {
            return Result.Failure<(Post, RecommendPost)>(PostErrors.Unauthorized);
        }

        var suggestion = await recommendPostRepository.GetByOriginalPostIdForUpdateAsync(postId, cancellationToken);
        if (suggestion is null)
        {
            return Result.Failure<(Post, RecommendPost)>(ImprovePostReviewErrors.NotFound);
        }

        if (suggestion.UserId != userId)
        {
            return Result.Failure<(Post, RecommendPost)>(PostErrors.Unauthorized);
        }

        return Result.Success((post, suggestion));
    }

    internal static void ClearSuggestion(
        Post post,
        RecommendPost suggestion,
        IPostRepository postRepository,
        IRecommendPostRepository recommendPostRepository)
    {
        post.RecommendPostId = null;
        post.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        postRepository.Update(post);
        recommendPostRepository.Remove(suggestion);
    }
}

public sealed class RejectImprovePostCommandHandler
    : IRequestHandler<RejectImprovePostCommand, Result<PostResponse>>
{
    private readonly IPostRepository _postRepository;
    private readonly IRecommendPostRepository _recommendPostRepository;
    private readonly PostResponseBuilder _postResponseBuilder;

    public RejectImprovePostCommandHandler(
        IPostRepository postRepository,
        IRecommendPostRepository recommendPostRepository,
        PostResponseBuilder postResponseBuilder)
    {
        _postRepository = postRepository;
        _recommendPostRepository = recommendPostRepository;
        _postResponseBuilder = postResponseBuilder;
    }

    public async Task<Result<PostResponse>> Handle(
        RejectImprovePostCommand request,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadPostAndSuggestionAsync(request.UserId, request.PostId, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<PostResponse>(loaded.Error);
        }

        var (post, suggestion) = loaded.Value;
        if (!IsTerminalStatus(suggestion.Status))
        {
            return Result.Failure<PostResponse>(ImprovePostReviewErrors.NotFinished);
        }

        ApproveImprovePostCommandHandler.ClearSuggestion(
            post,
            suggestion,
            _postRepository,
            _recommendPostRepository);
        await _recommendPostRepository.SaveChangesAsync(cancellationToken);

        var response = await _postResponseBuilder.BuildAsync(request.UserId, post, cancellationToken);
        return Result.Success(response);
    }

    private async Task<Result<(Post Post, RecommendPost Suggestion)>> LoadPostAndSuggestionAsync(
        Guid userId,
        Guid postId,
        CancellationToken cancellationToken)
    {
        var post = await _postRepository.GetByIdForUpdateAsync(postId, cancellationToken);
        if (post is null || post.DeletedAt.HasValue)
        {
            return Result.Failure<(Post, RecommendPost)>(PostErrors.NotFound);
        }

        if (post.UserId != userId)
        {
            return Result.Failure<(Post, RecommendPost)>(PostErrors.Unauthorized);
        }

        var suggestion = await _recommendPostRepository.GetByOriginalPostIdForUpdateAsync(postId, cancellationToken);
        if (suggestion is null)
        {
            return Result.Failure<(Post, RecommendPost)>(ImprovePostReviewErrors.NotFound);
        }

        if (suggestion.UserId != userId)
        {
            return Result.Failure<(Post, RecommendPost)>(PostErrors.Unauthorized);
        }

        return Result.Success((post, suggestion));
    }

    private static bool IsTerminalStatus(string? status)
    {
        return string.Equals(status, RecommendPostStatuses.Completed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, RecommendPostStatuses.Failed, StringComparison.OrdinalIgnoreCase);
    }
}

internal static class ImprovePostReviewErrors
{
    public static readonly Error NotFound = new(
        "ImprovePost.NotFound",
        "No improve-post suggestion exists for this post.");

    public static readonly Error NotCompleted = new(
        "ImprovePost.NotCompleted",
        "The improved post is not ready to approve yet.");

    public static readonly Error NotFinished = new(
        "ImprovePost.NotFinished",
        "The improved post is still running and cannot be rejected yet.");

    public static readonly Error MissingResultCaption = new(
        "ImprovePost.MissingResultCaption",
        "The improved post did not produce a replacement caption.");

    public static readonly Error MissingResultResource = new(
        "ImprovePost.MissingResultResource",
        "The improved post did not produce a replacement image.");
}
