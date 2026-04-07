using Application.Abstractions.Facebook;
using Application.Abstractions.Instagram;
using Application.Abstractions.SocialMedias;
using Application.Abstractions.Threads;
using Application.Abstractions.TikTok;
using Application.Posts.Models;
using Application.Posts.Queries;
using Domain.Entities;
using Domain.Repositories;
using FluentAssertions;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace AiMicroservice.Tests.Application.Posts.Queries;

public sealed class TikTokPlatformPostQueriesTests
{
    [Fact]
    public async Task GetPlatformPosts_ShouldReturnTikTokMetricsWithoutUnsupportedSaveMetric()
    {
        var userId = Guid.NewGuid();
        var socialMediaId = Guid.NewGuid();

        var facebookContentService = new Mock<IFacebookContentService>();
        var instagramContentService = new Mock<IInstagramContentService>();
        var userSocialMediaService = new Mock<IUserSocialMediaService>();
        var tikTokContentService = new Mock<ITikTokContentService>();
        var threadsContentService = new Mock<IThreadsContentService>();
        var postMetricSnapshotRepository = new Mock<IPostMetricSnapshotRepository>();

        userSocialMediaService
            .Setup(service => service.GetSocialMediasAsync(
                userId,
                It.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { socialMediaId })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<UserSocialMediaResult>>(
                new[]
                {
                    new UserSocialMediaResult(
                        socialMediaId,
                        "tiktok",
                        """{"access_token":"tt-token","scope":"video.list,user.info.stats"}""")
                }));

        tikTokContentService
            .Setup(service => service.GetVideosAsync(
                It.Is<TikTokVideoListRequest>(request =>
                    request.AccessToken == "tt-token" &&
                    request.Cursor == null &&
                    request.MaxCount == 10),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new TikTokVideoPageResult(
                Videos:
                [
                    new TikTokVideoDetails(
                        Id: "video-1",
                        Title: "Launch clip",
                        VideoDescription: "Caption",
                        CoverImageUrl: "https://cdn.example.com/cover.jpg",
                        ShareUrl: "https://tiktok.com/@creator/video/video-1",
                        EmbedLink: "https://tiktok.com/embed/video-1",
                        Duration: 15,
                        CreateTime: 1775074141,
                        ViewCount: 420,
                        LikeCount: 32,
                        CommentCount: 5,
                        ShareCount: 3)
                ],
                Cursor: 1775074141000,
                HasMore: false)));

        postMetricSnapshotRepository
            .Setup(repository => repository.GetLatestByPlatformPostIdsAsync(
                userId,
                socialMediaId,
                It.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(new[] { "video-1" })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PostMetricSnapshot>());

        var handler = new GetSocialMediaPlatformPostsQueryHandler(
            facebookContentService.Object,
            instagramContentService.Object,
            userSocialMediaService.Object,
            tikTokContentService.Object,
            threadsContentService.Object,
            postMetricSnapshotRepository.Object);

        var result = await handler.Handle(
            new GetSocialMediaPlatformPostsQuery(userId, socialMediaId, null, 10),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Platform.Should().Be("tiktok");
        result.Value.Items.Should().ContainSingle();
        result.Value.Items[0].Stats.Should().BeEquivalentTo(new SocialPlatformPostStatsResponse(
            Views: 420,
            Reach: 420,
            Impressions: 420,
            Likes: 32,
            Comments: 5,
            Replies: null,
            Shares: 3,
            Reposts: null,
            Quotes: null,
            TotalInteractions: 40,
            Saves: null,
            MetricBreakdown: new Dictionary<string, long>
            {
                ["views"] = 420,
                ["likes"] = 32,
                ["comments"] = 5,
                ["shares"] = 3
            }));

        result.Value.Items[0].Stats!.MetricBreakdown.Should().NotContainKey("favorites");
    }

    [Fact]
    public async Task GetPlatformPostAnalytics_ShouldReturnTikTokAccountInsightsAndZeroMetricHighlight()
    {
        var userId = Guid.NewGuid();
        var socialMediaId = Guid.NewGuid();
        const string postId = "video-1";

        var facebookContentService = new Mock<IFacebookContentService>();
        var instagramContentService = new Mock<IInstagramContentService>();
        var userSocialMediaService = new Mock<IUserSocialMediaService>();
        var tikTokContentService = new Mock<ITikTokContentService>();
        var threadsContentService = new Mock<IThreadsContentService>();
        var postMetricSnapshotRepository = new Mock<IPostMetricSnapshotRepository>();

        userSocialMediaService
            .Setup(service => service.GetSocialMediasAsync(
                userId,
                It.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { socialMediaId })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<UserSocialMediaResult>>(
                new[]
                {
                    new UserSocialMediaResult(
                        socialMediaId,
                        "tiktok",
                        """{"access_token":"tt-token","scope":"video.list,user.info.stats"}""")
                }));

        tikTokContentService
            .Setup(service => service.GetVideoAsync(
                It.Is<TikTokVideoDetailsRequest>(request =>
                    request.AccessToken == "tt-token" &&
                    request.VideoId == postId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new TikTokVideoDetails(
                Id: postId,
                Title: "Launch clip",
                VideoDescription: "Caption",
                CoverImageUrl: "https://cdn.example.com/cover.jpg",
                ShareUrl: "https://tiktok.com/@creator/video/video-1",
                EmbedLink: "https://tiktok.com/embed/video-1",
                Duration: 15,
                CreateTime: 1775074141,
                ViewCount: 0,
                LikeCount: 0,
                CommentCount: 0,
                ShareCount: 0)));

        tikTokContentService
            .Setup(service => service.GetAccountInsightsAsync(
                It.Is<TikTokAccountInsightsRequest>(request => request.AccessToken == "tt-token"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new TikTokAccountInsights(
                OpenId: "open-123",
                DisplayName: "Creator",
                AvatarUrl: "https://cdn.example.com/avatar.jpg",
                BioDescription: "Bio",
                FollowerCount: 321,
                FollowingCount: 12,
                LikesCount: 456,
                VideoCount: 7)));

        postMetricSnapshotRepository
            .Setup(repository => repository.GetLatestForUpdateAsync(
                userId,
                socialMediaId,
                postId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PostMetricSnapshot?)null);

        postMetricSnapshotRepository
            .Setup(repository => repository.AddAsync(
                It.IsAny<PostMetricSnapshot>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        postMetricSnapshotRepository
            .Setup(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = new GetSocialMediaPlatformPostAnalyticsQueryHandler(
            facebookContentService.Object,
            instagramContentService.Object,
            userSocialMediaService.Object,
            tikTokContentService.Object,
            threadsContentService.Object,
            postMetricSnapshotRepository.Object);

        var result = await handler.Handle(
            new GetSocialMediaPlatformPostAnalyticsQuery(userId, socialMediaId, postId, Refresh: true),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Platform.Should().Be("tiktok");
        result.Value.Stats.Saves.Should().BeNull();
        result.Value.Stats.MetricBreakdown.Should().NotContainKey("favorites");
        result.Value.Analysis.Highlights.Should().Contain("Tracked metrics are currently zero at the provider's latest sync.");
        result.Value.AccountInsights.Should().NotBeNull();
        result.Value.AccountInsights!.Metadata.Should().Contain(new KeyValuePair<string, string>("likesCount", "456"));
        result.Value.AccountInsights!.Metadata.Should().Contain(new KeyValuePair<string, string>("avatarUrl", "https://cdn.example.com/avatar.jpg"));
        result.Value.AccountInsights!.Metadata.Should().Contain(new KeyValuePair<string, string>("bio", "Bio"));
    }
}
