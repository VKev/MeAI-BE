using Application.Analytics.Queries;
using Grpc.Core;
using MediatR;
using SharedLibrary.Grpc.FeedAnalytics;

namespace WebApi.Grpc;

public sealed class FeedAnalyticsGrpcService : FeedAnalyticsService.FeedAnalyticsServiceBase
{
    private readonly IMediator _mediator;

    public FeedAnalyticsGrpcService(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override async Task<GetFeedDashboardSummaryResponse> GetDashboardSummary(
        GetFeedDashboardSummaryRequest request,
        ServerCallContext context)
    {
        var requestingUserId = ParseOptionalGuid(request.RequesterUserId, "Invalid requesterUserId.");
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Username is required."));
        }

        var result = await _mediator.Send(
            new GetFeedDashboardSummaryQuery(request.Username, requestingUserId, request.PostLimit > 0 ? request.PostLimit : null),
            context.CancellationToken);

        if (result.IsFailure)
        {
            throw new RpcException(new Status(StatusCode.NotFound, result.Error.Description));
        }

        var response = new GetFeedDashboardSummaryResponse
        {
            Profile = MapProfile(result.Value.Profile),
            FetchedPostCount = result.Value.FetchedPostCount,
            HasMorePosts = result.Value.HasMorePosts,
            LatestPublishedPostId = result.Value.LatestPublishedPostId?.ToString() ?? string.Empty,
            LatestPublishedAt = result.Value.LatestPublishedAt?.ToString("O") ?? string.Empty,
            AggregatedStats = MapStats(result.Value.AggregatedStats)
        };

        response.Posts.AddRange(result.Value.Posts.Select(MapPost));
        return response;
    }

    public override async Task<GetFeedPostAnalyticsResponse> GetPostAnalytics(
        GetFeedPostAnalyticsRequest request,
        ServerCallContext context)
    {
        ParseOptionalGuid(request.RequesterUserId, "Invalid requesterUserId.");
        if (!Guid.TryParse(request.PostId, out var postId) || postId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid postId."));
        }

        var result = await _mediator.Send(
            new GetFeedPostAnalyticsQuery(postId, ParseOptionalGuid(request.RequesterUserId, "Invalid requesterUserId."), request.CommentSampleLimit > 0 ? request.CommentSampleLimit : null),
            context.CancellationToken);

        if (result.IsFailure)
        {
            throw new RpcException(new Status(StatusCode.NotFound, result.Error.Description));
        }

        var response = new GetFeedPostAnalyticsResponse
        {
            Profile = MapProfile(result.Value.Profile),
            Post = MapPost(result.Value.Post)
        };

        response.CommentSamples.AddRange(result.Value.CommentSamples.Select(MapComment));
        return response;
    }

    private static Guid? ParseOptionalGuid(string? value, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Guid.TryParse(value, out var parsed) || parsed == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, errorMessage));
        }

        return parsed;
    }

    private static FeedAnalyticsProfile MapProfile(Application.Analytics.Models.FeedAnalyticsProfileResponse profile)
    {
        return new FeedAnalyticsProfile
        {
            UserId = profile.UserId.ToString(),
            Username = profile.Username,
            FullName = profile.FullName ?? string.Empty,
            AvatarUrl = profile.AvatarUrl ?? string.Empty,
            FollowersCount = profile.FollowersCount,
            FollowingCount = profile.FollowingCount,
            MediaCount = profile.MediaCount
        };
    }

    private static FeedAnalyticsStats MapStats(Application.Analytics.Models.FeedPostStatsResponse stats)
    {
        return new FeedAnalyticsStats
        {
            Likes = stats.Likes,
            TopLevelComments = stats.TopLevelComments,
            Replies = stats.Replies,
            TotalDiscussion = stats.TotalDiscussion,
            TotalInteractions = stats.TotalInteractions,
            MediaCount = stats.MediaCount,
            HashtagCount = stats.HashtagCount
        };
    }

    private static FeedDashboardPost MapPost(Application.Analytics.Models.FeedDashboardPostResponse post)
    {
        var response = new FeedDashboardPost
        {
            PostId = post.PostId.ToString(),
            UserId = post.UserId.ToString(),
            Content = post.Content ?? string.Empty,
            MediaUrl = post.MediaUrl ?? string.Empty,
            MediaType = post.MediaType ?? string.Empty,
            CreatedAt = post.CreatedAt?.ToString("O") ?? string.Empty,
            UpdatedAt = post.UpdatedAt?.ToString("O") ?? string.Empty,
            Stats = MapStats(post.Stats)
        };

        response.Hashtags.AddRange(post.Hashtags);
        return response;
    }

    private static FeedCommentSample MapComment(Application.Analytics.Models.FeedCommentSampleResponse comment)
    {
        return new FeedCommentSample
        {
            CommentId = comment.CommentId.ToString(),
            PostId = comment.PostId.ToString(),
            UserId = comment.UserId.ToString(),
            Username = comment.Username,
            AvatarUrl = comment.AvatarUrl ?? string.Empty,
            Content = comment.Content,
            LikesCount = comment.LikesCount,
            RepliesCount = comment.RepliesCount,
            CreatedAt = comment.CreatedAt?.ToString("O") ?? string.Empty,
            UpdatedAt = comment.UpdatedAt?.ToString("O") ?? string.Empty
        };
    }
}
