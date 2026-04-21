using Application.Abstractions.Data;
using Application.Abstractions.Resources;
using Application.Analytics.Models;
using Application.Common;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Analytics.Queries;

public sealed record GetFeedPostAnalyticsQuery(
    Guid PostId,
    Guid? RequestingUserId,
    int? CommentSampleLimit) : IQuery<FeedPostAnalyticsResponse>;

public sealed class GetFeedPostAnalyticsQueryHandler
    : IQueryHandler<GetFeedPostAnalyticsQuery, FeedPostAnalyticsResponse>
{
    private const int DefaultCommentSampleLimit = 5;
    private const int MaxCommentSampleLimit = 10;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserResourceService _userResourceService;

    public GetFeedPostAnalyticsQueryHandler(IUnitOfWork unitOfWork, IUserResourceService userResourceService)
    {
        _unitOfWork = unitOfWork;
        _userResourceService = userResourceService;
    }

    public async Task<Result<FeedPostAnalyticsResponse>> Handle(
        GetFeedPostAnalyticsQuery request,
        CancellationToken cancellationToken)
    {
        var post = await _unitOfWork.Repository<Post>()
            .GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Id == request.PostId && !item.IsDeleted && item.DeletedAt == null,
                cancellationToken);

        if (post is null)
        {
            return Result.Failure<FeedPostAnalyticsResponse>(FeedErrors.PostNotFound);
        }

        var authorProfilesResult = await _userResourceService.GetPublicUserProfilesByIdsAsync(new[] { post.UserId }, cancellationToken);
        if (authorProfilesResult.IsFailure || !authorProfilesResult.Value.TryGetValue(post.UserId, out var authorProfile))
        {
            return Result.Failure<FeedPostAnalyticsResponse>(MapUserProfileError(authorProfilesResult.IsFailure
                ? authorProfilesResult.Error
                : FeedErrors.UserNotFound));
        }

        var hashtagsByPostId = await FeedPostSupport.LoadHashtagsByPostIdsAsync(_unitOfWork, new[] { post.Id }, cancellationToken);
        var mediaByPostId = await FeedPostSupport.LoadPresignedMediaByPostIdsAsync(_userResourceService, new[] { post }, cancellationToken);

        var commentStats = await _unitOfWork.Repository<Comment>()
            .GetAll()
            .AsNoTracking()
            .Where(comment =>
                comment.PostId == post.Id &&
                !comment.IsDeleted &&
                comment.DeletedAt == null)
            .GroupBy(comment => comment.PostId)
            .Select(group => new
            {
                PostId = group.Key,
                TopLevelComments = group.LongCount(comment => comment.ParentCommentId == null),
                Replies = group.LongCount(comment => comment.ParentCommentId != null),
                TotalDiscussion = group.LongCount()
            })
            .FirstOrDefaultAsync(cancellationToken);

        var profileTask = BuildProfileAsync(authorProfile, cancellationToken);
        var commentSamplesTask = BuildCommentSamplesAsync(post.Id, request.CommentSampleLimit, cancellationToken);

        await Task.WhenAll(profileTask, commentSamplesTask);

        var hashtags = hashtagsByPostId.TryGetValue(post.Id, out var hashtagList)
            ? hashtagList
            : Array.Empty<string>();
        var media = mediaByPostId.TryGetValue(post.Id, out var mediaList)
            ? mediaList
            : Array.Empty<UserResourcePresignResult>();
        var primaryMedia = media.FirstOrDefault();
        var topLevelComments = commentStats?.TopLevelComments ?? 0;
        var replies = commentStats?.Replies ?? 0;
        var totalDiscussion = commentStats?.TotalDiscussion ?? 0;
        var mediaCount = (post.ResourceIds ?? Array.Empty<Guid>())
            .Where(id => id != Guid.Empty)
            .Distinct()
            .LongCount();
        var hashtagCount = hashtags.LongCount();

        var postResponse = new FeedDashboardPostResponse(
            PostId: post.Id,
            UserId: post.UserId,
            Content: post.Content,
            MediaUrl: primaryMedia?.PresignedUrl ?? post.MediaUrl,
            MediaType: post.MediaType ?? primaryMedia?.ResourceType ?? primaryMedia?.ContentType,
            Hashtags: hashtags,
            CreatedAt: ToDateTimeOffset(post.CreatedAt),
            UpdatedAt: ToDateTimeOffset(post.UpdatedAt),
            Stats: new FeedPostStatsResponse(
                Likes: post.LikesCount,
                TopLevelComments: topLevelComments,
                Replies: replies,
                TotalDiscussion: totalDiscussion,
                TotalInteractions: post.LikesCount + topLevelComments + replies,
                MediaCount: mediaCount,
                HashtagCount: hashtagCount));

        return Result.Success(new FeedPostAnalyticsResponse(
            Profile: profileTask.Result,
            Post: postResponse,
            CommentSamples: commentSamplesTask.Result));
    }

    private async Task<FeedAnalyticsProfileResponse> BuildProfileAsync(
        PublicUserProfileResult profile,
        CancellationToken cancellationToken)
    {
        var follows = _unitOfWork.Repository<Follow>()
            .GetAll()
            .AsNoTracking();

        var followersTask = follows.LongCountAsync(item => item.FolloweeId == profile.UserId, cancellationToken);
        var followingTask = follows.LongCountAsync(item => item.FollowerId == profile.UserId, cancellationToken);
        var mediaCountTask = _unitOfWork.Repository<Post>()
            .GetAll()
            .AsNoTracking()
            .LongCountAsync(
                item => item.UserId == profile.UserId && !item.IsDeleted && item.DeletedAt == null,
                cancellationToken);

        await Task.WhenAll(followersTask, followingTask, mediaCountTask);

        return new FeedAnalyticsProfileResponse(
            profile.UserId,
            profile.Username,
            profile.FullName,
            profile.AvatarUrl,
            followersTask.Result,
            followingTask.Result,
            mediaCountTask.Result);
    }

    private async Task<IReadOnlyList<FeedCommentSampleResponse>> BuildCommentSamplesAsync(
        Guid postId,
        int? requestedLimit,
        CancellationToken cancellationToken)
    {
        var normalizedLimit = Math.Clamp(requestedLimit ?? DefaultCommentSampleLimit, 1, MaxCommentSampleLimit);
        var comments = await _unitOfWork.Repository<Comment>()
            .GetAll()
            .AsNoTracking()
            .Where(comment =>
                comment.PostId == postId &&
                comment.ParentCommentId == null &&
                !comment.IsDeleted &&
                comment.DeletedAt == null)
            .OrderByDescending(comment => comment.CreatedAt)
            .ThenByDescending(comment => comment.Id)
            .Take(normalizedLimit)
            .ToListAsync(cancellationToken);

        if (comments.Count == 0)
        {
            return Array.Empty<FeedCommentSampleResponse>();
        }

        var authors = await _userResourceService.GetPublicUserProfilesByIdsAsync(
            comments.Select(comment => comment.UserId).Distinct().ToList(),
            cancellationToken);

        if (authors.IsFailure)
        {
            return comments
                .Select(comment => new FeedCommentSampleResponse(
                    comment.Id,
                    comment.PostId,
                    comment.UserId,
                    comment.UserId.ToString(),
                    null,
                    comment.Content,
                    comment.LikesCount,
                    comment.RepliesCount,
                    ToDateTimeOffset(comment.CreatedAt),
                    ToDateTimeOffset(comment.UpdatedAt)))
                .ToList();
        }

        return comments
            .Select(comment =>
            {
                var profile = authors.Value.TryGetValue(comment.UserId, out var resolvedProfile)
                    ? resolvedProfile
                    : new PublicUserProfileResult(comment.UserId, comment.UserId.ToString(), null, null);

                return new FeedCommentSampleResponse(
                    CommentId: comment.Id,
                    PostId: comment.PostId,
                    UserId: comment.UserId,
                    Username: profile.Username,
                    AvatarUrl: profile.AvatarUrl,
                    Content: comment.Content,
                    LikesCount: comment.LikesCount,
                    RepliesCount: comment.RepliesCount,
                    CreatedAt: ToDateTimeOffset(comment.CreatedAt),
                    UpdatedAt: ToDateTimeOffset(comment.UpdatedAt));
            })
            .ToList();
    }

    private static DateTimeOffset? ToDateTimeOffset(DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        var normalized = value.Value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
            : value.Value;

        return new DateTimeOffset(normalized);
    }

    private static Error MapUserProfileError(Error error)
    {
        return string.Equals(error.Code, "UserResources.NotFound", StringComparison.Ordinal)
            ? FeedErrors.UserNotFound
            : error;
    }
}
