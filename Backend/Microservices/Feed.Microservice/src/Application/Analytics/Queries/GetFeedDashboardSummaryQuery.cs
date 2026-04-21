using Application.Abstractions.Data;
using Application.Abstractions.Resources;
using Application.Analytics.Models;
using Application.Common;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Analytics.Queries;

public sealed record GetFeedDashboardSummaryQuery(
    string Username,
    Guid? RequestingUserId,
    int? PostLimit) : IQuery<FeedDashboardSummaryResponse>;

public sealed class GetFeedDashboardSummaryQueryHandler
    : IQueryHandler<GetFeedDashboardSummaryQuery, FeedDashboardSummaryResponse>
{
    private const int DefaultPostLimit = 5;
    private const int MaxPostLimit = 8;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserResourceService _userResourceService;

    public GetFeedDashboardSummaryQueryHandler(IUnitOfWork unitOfWork, IUserResourceService userResourceService)
    {
        _unitOfWork = unitOfWork;
        _userResourceService = userResourceService;
    }

    public async Task<Result<FeedDashboardSummaryResponse>> Handle(
        GetFeedDashboardSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var username = FeedModerationSupport.NormalizeUsername(request.Username);
        if (username is null)
        {
            return Result.Failure<FeedDashboardSummaryResponse>(FeedErrors.InvalidUsername);
        }

        var profileResult = await _userResourceService.GetPublicUserProfileByUsernameAsync(username, cancellationToken);
        if (profileResult.IsFailure)
        {
            return Result.Failure<FeedDashboardSummaryResponse>(MapUserProfileError(profileResult.Error));
        }

        var targetUserId = profileResult.Value.UserId;
        var normalizedLimit = Math.Clamp(request.PostLimit ?? DefaultPostLimit, 1, MaxPostLimit);
        var requestedTake = normalizedLimit + 1;

        var postsQuery = _unitOfWork.Repository<Post>()
            .GetAll()
            .AsNoTracking()
            .Where(post =>
                !post.IsDeleted &&
                post.DeletedAt == null &&
                post.UserId == targetUserId)
            .OrderByDescending(post => post.CreatedAt)
            .ThenByDescending(post => post.Id);

        var posts = await postsQuery
            .Take(requestedTake)
            .ToListAsync(cancellationToken);

        var hasMorePosts = posts.Count > normalizedLimit;
        var visiblePosts = hasMorePosts
            ? posts.Take(normalizedLimit).ToList()
            : posts;

        var profile = await BuildProfileAsync(profileResult.Value, cancellationToken);
        var dashboardPosts = await BuildDashboardPostsAsync(visiblePosts, cancellationToken);
        var latestPost = dashboardPosts.FirstOrDefault();

        return Result.Success(new FeedDashboardSummaryResponse(
            Profile: profile,
            FetchedPostCount: dashboardPosts.Count,
            HasMorePosts: hasMorePosts,
            LatestPublishedPostId: latestPost?.PostId,
            LatestPublishedAt: latestPost?.CreatedAt,
            AggregatedStats: BuildAggregatedStats(dashboardPosts),
            Posts: dashboardPosts));
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

    private async Task<IReadOnlyList<FeedDashboardPostResponse>> BuildDashboardPostsAsync(
        IReadOnlyList<Post> posts,
        CancellationToken cancellationToken)
    {
        if (posts.Count == 0)
        {
            return Array.Empty<FeedDashboardPostResponse>();
        }

        var postIds = posts.Select(post => post.Id).ToList();
        var hashtagsByPostId = await FeedPostSupport.LoadHashtagsByPostIdsAsync(_unitOfWork, postIds, cancellationToken);
        var mediaByPostId = await FeedPostSupport.LoadPresignedMediaByPostIdsAsync(_userResourceService, posts, cancellationToken);

        var commentStats = await _unitOfWork.Repository<Comment>()
            .GetAll()
            .AsNoTracking()
            .Where(comment =>
                postIds.Contains(comment.PostId) &&
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
            .ToDictionaryAsync(item => item.PostId, cancellationToken);

        return posts
            .Select(post =>
            {
                var hashtags = hashtagsByPostId.TryGetValue(post.Id, out var hashtagList)
                    ? hashtagList
                    : Array.Empty<string>();
                var media = mediaByPostId.TryGetValue(post.Id, out var mediaList)
                    ? mediaList
                    : Array.Empty<UserResourcePresignResult>();
                var primaryMedia = media.FirstOrDefault();
                commentStats.TryGetValue(post.Id, out var postCommentStats);

                var topLevelComments = postCommentStats?.TopLevelComments ?? 0;
                var replies = postCommentStats?.Replies ?? 0;
                var totalDiscussion = postCommentStats?.TotalDiscussion ?? 0;
                var mediaCount = (post.ResourceIds ?? Array.Empty<Guid>())
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .LongCount();
                var hashtagCount = hashtags.LongCount();

                return new FeedDashboardPostResponse(
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
            })
            .ToList();
    }

    private static FeedPostStatsResponse BuildAggregatedStats(
        IReadOnlyList<FeedDashboardPostResponse> posts)
    {
        return new FeedPostStatsResponse(
            Likes: posts.Sum(post => post.Stats.Likes),
            TopLevelComments: posts.Sum(post => post.Stats.TopLevelComments),
            Replies: posts.Sum(post => post.Stats.Replies),
            TotalDiscussion: posts.Sum(post => post.Stats.TotalDiscussion),
            TotalInteractions: posts.Sum(post => post.Stats.TotalInteractions),
            MediaCount: posts.Sum(post => post.Stats.MediaCount),
            HashtagCount: posts.Sum(post => post.Stats.HashtagCount));
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
