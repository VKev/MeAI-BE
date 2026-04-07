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

public sealed class DashboardSummaryQueryTests
{
    [Fact]
    public async Task GetDashboardSummary_ShouldReturnTikTokAggregatedSummary()
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
                    request.MaxCount == 5),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new TikTokVideoPageResult(
                Videos:
                [
                    new TikTokVideoDetails(
                        Id: "video-1",
                        Title: "First",
                        VideoDescription: "Caption 1",
                        CoverImageUrl: "https://cdn.example.com/cover-1.jpg",
                        ShareUrl: "https://tiktok.com/@creator/video/video-1",
                        EmbedLink: null,
                        Duration: 12,
                        CreateTime: 1775074141,
                        ViewCount: 200,
                        LikeCount: 20,
                        CommentCount: 5,
                        ShareCount: 2),
                    new TikTokVideoDetails(
                        Id: "video-2",
                        Title: "Second",
                        VideoDescription: "Caption 2",
                        CoverImageUrl: "https://cdn.example.com/cover-2.jpg",
                        ShareUrl: "https://tiktok.com/@creator/video/video-2",
                        EmbedLink: null,
                        Duration: 18,
                        CreateTime: 1775074000,
                        ViewCount: 120,
                        LikeCount: 12,
                        CommentCount: 3,
                        ShareCount: 1)
                ],
                Cursor: 1775074141000,
                HasMore: true)));

        tikTokContentService
            .Setup(service => service.GetAccountInsightsAsync(
                It.Is<TikTokAccountInsightsRequest>(request => request.AccessToken == "tt-token"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new TikTokAccountInsights(
                OpenId: "open-1",
                DisplayName: "Creator",
                AvatarUrl: "https://cdn.example.com/avatar.jpg",
                BioDescription: "Bio",
                FollowerCount: 321,
                FollowingCount: 12,
                LikesCount: 456,
                VideoCount: 7)));

        postMetricSnapshotRepository
            .Setup(repository => repository.GetLatestByPlatformPostIdsAsync(
                userId,
                socialMediaId,
                It.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(new[] { "video-1", "video-2" })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PostMetricSnapshot>());

        var handler = new GetSocialMediaDashboardSummaryQueryHandler(
            facebookContentService.Object,
            instagramContentService.Object,
            userSocialMediaService.Object,
            tikTokContentService.Object,
            threadsContentService.Object,
            postMetricSnapshotRepository.Object);

        var result = await handler.Handle(
            new GetSocialMediaDashboardSummaryQuery(userId, socialMediaId, 5),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Platform.Should().Be("tiktok");
        result.Value.FetchedPostCount.Should().Be(2);
        result.Value.HasMorePosts.Should().BeTrue();
        result.Value.LatestPublishedPostId.Should().Be("video-1");
        result.Value.AggregatedStats.Should().BeEquivalentTo(new SocialPlatformPostStatsResponse(
            Views: 320,
            Reach: 320,
            Impressions: 320,
            Likes: 32,
            Comments: 8,
            Replies: 0,
            Shares: 3,
            Reposts: 0,
            Quotes: 0,
            TotalInteractions: 43,
            Saves: null));
        result.Value.AccountInsights!.Metadata.Should().Contain(new KeyValuePair<string, string>("avatarUrl", "https://cdn.example.com/avatar.jpg"));
    }

    [Fact]
    public async Task GetDashboardSummary_ShouldUseThreadsPostProfilePictureFallback()
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
                        "threads",
                        """{"access_token":"threads-token"}""")
                }));

        threadsContentService
            .Setup(service => service.GetPostsAsync(
                It.Is<ThreadsPostListRequest>(request =>
                    request.AccessToken == "threads-token" &&
                    request.Limit == 5 &&
                    request.Cursor == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new ThreadsPostPageResult(
                Posts:
                [
                    new ThreadsPostDetails(
                        Id: "thread-1",
                        MediaProductType: "THREADS",
                        MediaType: "TEXT_POST",
                        MediaUrl: null,
                        GifUrl: null,
                        Permalink: "https://threads.net/t/thread-1",
                        Username: "threads-user",
                        Text: "Hello Threads",
                        Timestamp: "2026-04-02T08:00:00+0000",
                        Shortcode: null,
                        ThumbnailUrl: null,
                        IsQuotePost: false,
                        HasReplies: true,
                        AltText: null,
                        LinkAttachmentUrl: null,
                        TopicTag: null,
                        ProfilePictureUrl: "https://cdn.example.com/threads-avatar.jpg")
                ],
                NextCursor: null,
                HasMore: false)));

        threadsContentService
            .Setup(service => service.GetAccountInsightsAsync(
                It.Is<ThreadsAccountInsightsRequest>(request => request.AccessToken == "threads-token"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new ThreadsAccountInsights(
                Id: "threads-account",
                Username: null,
                Name: "Threads Creator",
                Biography: "Bio",
                ProfilePictureUrl: null,
                Followers: 77)));

        threadsContentService
            .Setup(service => service.GetPostInsightsAsync(
                It.Is<ThreadsPostInsightsRequest>(request =>
                    request.AccessToken == "threads-token" &&
                    request.PostId == "thread-1"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new ThreadsPostInsights(
                Views: 90,
                Likes: 11,
                Replies: 4,
                Reposts: 2,
                Quotes: 1,
                Shares: 3)));

        postMetricSnapshotRepository
            .Setup(repository => repository.GetLatestByPlatformPostIdsAsync(
                userId,
                socialMediaId,
                It.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(new[] { "thread-1" })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PostMetricSnapshot>());

        var handler = new GetSocialMediaDashboardSummaryQueryHandler(
            facebookContentService.Object,
            instagramContentService.Object,
            userSocialMediaService.Object,
            tikTokContentService.Object,
            threadsContentService.Object,
            postMetricSnapshotRepository.Object);

        var result = await handler.Handle(
            new GetSocialMediaDashboardSummaryQuery(userId, socialMediaId, 5),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Platform.Should().Be("threads");
        result.Value.AccountInsights.Should().NotBeNull();
        result.Value.AccountInsights!.Username.Should().Be("threads-user");
        result.Value.AccountInsights.Metadata.Should().Contain(
            new KeyValuePair<string, string>("profilePictureUrl", "https://cdn.example.com/threads-avatar.jpg"));
        result.Value.AggregatedStats.TotalInteractions.Should().Be(21);
    }
}
