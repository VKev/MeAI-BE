using Application.Abstractions.Feed;
using Application.Posts.Models;
using Application.Posts.Queries;
using FluentAssertions;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace AiMicroservice.Tests.Application.Posts.Queries;

public sealed class FeedAnalyticsQueryTests
{
    [Fact]
    public async Task GetFeedDashboardSummary_ShouldDelegateToFeedAnalyticsService()
    {
        var requesterUserId = Guid.NewGuid();
        var dashboardResponse = new SocialPlatformDashboardSummaryResponse(
            SocialMediaId: requesterUserId,
            Platform: "feed",
            FetchedPostCount: 2,
            HasMorePosts: true,
            NextCursor: null,
            LatestPublishedPostId: "post-1",
            LatestPublishedAt: DateTimeOffset.Parse("2026-04-21T09:00:00+00:00"),
            AggregatedStats: new SocialPlatformPostStatsResponse(
                Views: null,
                Likes: 20,
                Comments: 5,
                Replies: 3,
                TotalInteractions: 28,
                MetricBreakdown: new Dictionary<string, long>
                {
                    ["topLevelComments"] = 5,
                    ["replies"] = 3,
                    ["hashtagCount"] = 4
                }),
            LatestAnalysis: new SocialPlatformPostAnalysisResponse(
                EngagementRateByViews: null,
                ConversationRateByViews: null,
                AmplificationRateByViews: null,
                ApprovalRateByViews: null,
                PerformanceBand: "insufficient_data",
                Highlights: new[]
                {
                    "Provider response does not expose enough view data for rate-based analysis.",
                    "The post still recorded 28 tracked interactions."
                }),
            AccountInsights: new SocialPlatformAccountInsightsResponse(
                AccountId: requesterUserId.ToString(),
                AccountName: "Feed User",
                Username: "feed-user",
                Followers: 10,
                Following: 4,
                MediaCount: 12,
                Metadata: new Dictionary<string, string> { ["avatarUrl"] = "https://cdn.example.com/avatar.jpg" }),
            Posts: new[]
            {
                new SocialPlatformDashboardPostResponse(
                    new SocialPlatformPostSummaryResponse(
                        PlatformPostId: "post-1",
                        Title: null,
                        Text: "Feed content",
                        Description: null,
                        MediaType: "image",
                        MediaUrl: "https://cdn.example.com/post.jpg",
                        ThumbnailUrl: "https://cdn.example.com/post.jpg",
                        Permalink: null,
                        ShareUrl: null,
                        EmbedUrl: null,
                        DurationSeconds: null,
                        PublishedAt: DateTimeOffset.Parse("2026-04-21T09:00:00+00:00"),
                        Stats: new SocialPlatformPostStatsResponse(
                            Views: null,
                            Likes: 20,
                            Comments: 5,
                            Replies: 3,
                            TotalInteractions: 28,
                            MetricBreakdown: new Dictionary<string, long>
                            {
                                ["topLevelComments"] = 5,
                                ["replies"] = 3,
                                ["mediaCount"] = 1
                            })),
                    new SocialPlatformPostAnalysisResponse(
                        EngagementRateByViews: null,
                        ConversationRateByViews: null,
                        AmplificationRateByViews: null,
                        ApprovalRateByViews: null,
                        PerformanceBand: "insufficient_data",
                        Highlights: new[] { "The post still recorded 28 tracked interactions." }))
            });

        var feedAnalyticsService = new Mock<IFeedAnalyticsService>();
        feedAnalyticsService
            .Setup(service => service.GetDashboardSummaryAsync(
                requesterUserId,
                "feed-user",
                6,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(dashboardResponse));

        var handler = new GetFeedDashboardSummaryQueryHandler(feedAnalyticsService.Object);

        var result = await handler.Handle(
            new GetFeedDashboardSummaryQuery(requesterUserId, "feed-user", 6),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(dashboardResponse);
    }

    [Fact]
    public async Task GetFeedPostAnalytics_ShouldDelegateToFeedAnalyticsService()
    {
        var requesterUserId = Guid.NewGuid();
        var postId = Guid.NewGuid();
        var analyticsResponse = new SocialPlatformPostAnalyticsResponse(
            SocialMediaId: requesterUserId,
            Platform: "feed",
            PlatformPostId: postId.ToString(),
            Post: new SocialPlatformPostSummaryResponse(
                PlatformPostId: postId.ToString(),
                Title: null,
                Text: "Feed analytics",
                Description: null,
                MediaType: "image",
                MediaUrl: "https://cdn.example.com/post.jpg",
                ThumbnailUrl: "https://cdn.example.com/post.jpg",
                Permalink: null,
                ShareUrl: null,
                EmbedUrl: null,
                DurationSeconds: null,
                PublishedAt: DateTimeOffset.Parse("2026-04-21T09:00:00+00:00"),
                Stats: new SocialPlatformPostStatsResponse(
                    Views: null,
                    Likes: 14,
                    Comments: 4,
                    Replies: 2,
                    TotalInteractions: 20,
                    MetricBreakdown: new Dictionary<string, long>
                    {
                        ["topLevelComments"] = 4,
                        ["replies"] = 2,
                        ["hashtagCount"] = 2
                    })),
            Stats: new SocialPlatformPostStatsResponse(
                Views: null,
                Likes: 14,
                Comments: 4,
                Replies: 2,
                TotalInteractions: 20,
                MetricBreakdown: new Dictionary<string, long>
                {
                    ["topLevelComments"] = 4,
                    ["replies"] = 2,
                    ["mediaCount"] = 1
                }),
            Analysis: new SocialPlatformPostAnalysisResponse(
                EngagementRateByViews: null,
                ConversationRateByViews: null,
                AmplificationRateByViews: null,
                ApprovalRateByViews: null,
                PerformanceBand: "insufficient_data",
                Highlights: new[]
                {
                    "Provider response does not expose enough view data for rate-based analysis.",
                    "The post still recorded 20 tracked interactions."
                }),
            RetrievedAt: DateTimeOffset.UtcNow,
            AccountInsights: new SocialPlatformAccountInsightsResponse(
                AccountId: requesterUserId.ToString(),
                AccountName: "Feed User",
                Username: "feed-user",
                Followers: 8,
                Following: 3,
                MediaCount: 6),
            CommentSamples: new[]
            {
                new SocialPlatformCommentResponse(
                    Id: Guid.NewGuid().ToString(),
                    Text: "Interesting",
                    AuthorId: Guid.NewGuid().ToString(),
                    AuthorName: "Commenter",
                    AuthorUsername: "commenter",
                    CreatedAt: DateTimeOffset.Parse("2026-04-21T09:05:00+00:00"),
                    LikeCount: 2,
                    ReplyCount: 1,
                    Permalink: null)
            },
            AdditionalMetrics: new Dictionary<string, long>
            {
                ["topLevelComments"] = 4,
                ["replies"] = 2,
                ["hashtagCount"] = 2
            });

        var feedAnalyticsService = new Mock<IFeedAnalyticsService>();
        feedAnalyticsService
            .Setup(service => service.GetPostAnalyticsAsync(
                requesterUserId,
                postId,
                4,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(analyticsResponse));

        var handler = new GetFeedPostAnalyticsQueryHandler(feedAnalyticsService.Object);

        var result = await handler.Handle(
            new GetFeedPostAnalyticsQuery(requesterUserId, postId, 4),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(analyticsResponse);
    }
}
