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
    public async Task GetDashboardSummary_ShouldReturnFacebookAggregatedSummaryFromLiveMetrics()
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
                        "facebook",
                        """{"access_token":"fb-token"}""")
                }));

        facebookContentService
            .Setup(service => service.GetPostsAsync(
                It.Is<FacebookPostListRequest>(request =>
                    request.UserAccessToken == "fb-token" &&
                    request.Limit == 5 &&
                    request.Cursor == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new FacebookPostPageResult(
                Posts:
                [
                    new FacebookPostDetails(
                        Id: "123_456",
                        PageId: "123",
                        Message: "Launch update",
                        Story: null,
                        PermalinkUrl: "https://facebook.com/123_456",
                        CreatedTime: "2026-03-18T09:00:00+0000",
                        FullPictureUrl: "https://cdn.example.com/full-1.jpg",
                        MediaType: "image",
                        MediaUrl: "https://cdn.example.com/media-1.jpg",
                        ThumbnailUrl: "https://cdn.example.com/thumb-1.jpg",
                        AttachmentTitle: "Campaign launch",
                        AttachmentDescription: "Description",
                        ViewCount: null,
                        ReactionCount: null,
                        CommentCount: null,
                        ShareCount: 2),
                    new FacebookPostDetails(
                        Id: "123_789",
                        PageId: "123",
                        Message: "Customer story",
                        Story: null,
                        PermalinkUrl: "https://facebook.com/123_789",
                        CreatedTime: "2026-03-17T09:00:00+0000",
                        FullPictureUrl: "https://cdn.example.com/full-2.jpg",
                        MediaType: "video",
                        MediaUrl: "https://cdn.example.com/media-2.mp4",
                        ThumbnailUrl: "https://cdn.example.com/thumb-2.jpg",
                        AttachmentTitle: "Customer story",
                        AttachmentDescription: "Story description",
                        ViewCount: null,
                        ReactionCount: null,
                        CommentCount: null,
                        ShareCount: 1)
                ],
                NextCursor: "next",
                HasMore: true)));

        facebookContentService
            .Setup(service => service.GetPostAsync(
                It.Is<FacebookPostDetailsRequest>(request =>
                    request.UserAccessToken == "fb-token" &&
                    request.PostId == "123_456"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new FacebookPostDetails(
                Id: "123_456",
                PageId: "123",
                Message: "Launch update",
                Story: null,
                PermalinkUrl: "https://facebook.com/123_456",
                CreatedTime: "2026-03-18T09:00:00+0000",
                FullPictureUrl: "https://cdn.example.com/full-1.jpg",
                MediaType: "image",
                MediaUrl: "https://cdn.example.com/media-1.jpg",
                ThumbnailUrl: "https://cdn.example.com/thumb-1.jpg",
                AttachmentTitle: "Campaign launch",
                AttachmentDescription: "Description",
                ViewCount: 800,
                ReactionCount: 25,
                CommentCount: 7,
                ShareCount: 2,
                ReactionBreakdown: new Dictionary<string, long> { ["like"] = 20, ["love"] = 5 },
                ReachCount: 1200,
                ImpressionCount: null)));

        facebookContentService
            .Setup(service => service.GetPostAsync(
                It.Is<FacebookPostDetailsRequest>(request =>
                    request.UserAccessToken == "fb-token" &&
                    request.PostId == "123_789"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new FacebookPostDetails(
                Id: "123_789",
                PageId: "123",
                Message: "Customer story",
                Story: null,
                PermalinkUrl: "https://facebook.com/123_789",
                CreatedTime: "2026-03-17T09:00:00+0000",
                FullPictureUrl: "https://cdn.example.com/full-2.jpg",
                MediaType: "video",
                MediaUrl: "https://cdn.example.com/media-2.mp4",
                ThumbnailUrl: "https://cdn.example.com/thumb-2.jpg",
                AttachmentTitle: "Customer story",
                AttachmentDescription: "Story description",
                ViewCount: 400,
                ReactionCount: 10,
                CommentCount: 3,
                ShareCount: 1,
                ReactionBreakdown: new Dictionary<string, long> { ["like"] = 8, ["wow"] = 2 },
                ReachCount: 900,
                ImpressionCount: null)));

        facebookContentService
            .Setup(service => service.GetPageInsightsAsync(
                It.Is<FacebookPageInsightsRequest>(request => request.UserAccessToken == "fb-token"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new FacebookPageInsights(
                PageId: "123",
                Name: "MeAI Facebook",
                Followers: 500,
                Fans: 450)));

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
        result.Value.Platform.Should().Be("facebook");
        result.Value.FetchedPostCount.Should().Be(2);
        result.Value.HasMorePosts.Should().BeTrue();
        result.Value.LatestPublishedPostId.Should().Be("123_456");
        result.Value.AggregatedStats.Should().BeEquivalentTo(new SocialPlatformPostStatsResponse(
            Views: 1200,
            Reach: 2100,
            Impressions: null,
            Likes: 35,
            Comments: 10,
            Replies: 0,
            Shares: 3,
            Reposts: 0,
            Quotes: 0,
            TotalInteractions: 48,
            Saves: null));
        result.Value.AccountInsights!.Followers.Should().Be(500);
        result.Value.Posts[0].Post.Stats!.MetricBreakdown.Should().Contain(new KeyValuePair<string, long>("reach", 1200));
    }

    [Fact]
    public async Task GetDashboardSummary_ShouldKeepFacebookViewsNull_WhenOnlyReachIsAvailable()
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
                        "facebook",
                        """{"access_token":"fb-token"}""")
                }));

        facebookContentService
            .Setup(service => service.GetPostsAsync(
                It.Is<FacebookPostListRequest>(request => request.UserAccessToken == "fb-token"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new FacebookPostPageResult(
                Posts:
                [
                    new FacebookPostDetails(
                        Id: "123_111",
                        PageId: "123",
                        Message: "Photo post",
                        Story: null,
                        PermalinkUrl: "https://facebook.com/123_111",
                        CreatedTime: "2026-03-18T09:00:00+0000",
                        FullPictureUrl: null,
                        MediaType: "image",
                        MediaUrl: null,
                        ThumbnailUrl: null,
                        AttachmentTitle: null,
                        AttachmentDescription: null,
                        ViewCount: null,
                        ReactionCount: null,
                        CommentCount: null,
                        ShareCount: 1)
                ],
                NextCursor: null,
                HasMore: false)));

        facebookContentService
            .Setup(service => service.GetPostAsync(
                It.Is<FacebookPostDetailsRequest>(request =>
                    request.UserAccessToken == "fb-token" &&
                    request.PostId == "123_111"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new FacebookPostDetails(
                Id: "123_111",
                PageId: "123",
                Message: "Photo post",
                Story: null,
                PermalinkUrl: "https://facebook.com/123_111",
                CreatedTime: "2026-03-18T09:00:00+0000",
                FullPictureUrl: null,
                MediaType: "image",
                MediaUrl: null,
                ThumbnailUrl: null,
                AttachmentTitle: null,
                AttachmentDescription: null,
                ViewCount: null,
                ReactionCount: 12,
                CommentCount: 4,
                ShareCount: 1,
                ReachCount: 300,
                ImpressionCount: 500)));

        facebookContentService
            .Setup(service => service.GetPageInsightsAsync(
                It.Is<FacebookPageInsightsRequest>(request => request.UserAccessToken == "fb-token"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new FacebookPageInsights(
                PageId: "123",
                Name: "Facebook Page",
                Followers: 50,
                Fans: 40)));

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
        result.Value.AggregatedStats.Views.Should().BeNull();
        result.Value.AggregatedStats.Reach.Should().Be(300);
        result.Value.AggregatedStats.Impressions.Should().Be(500);
        result.Value.AggregatedStats.Likes.Should().Be(12);
        result.Value.AggregatedStats.Comments.Should().Be(4);
        result.Value.AggregatedStats.Shares.Should().Be(1);
    }

    [Fact]
    public async Task GetDashboardSummary_ShouldHydrateInstagramViewsFromLiveInsights()
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
                        "instagram",
                        """{"access_token":"ig-token","instagram_business_account_id":"ig-user"}""")
                }));

        instagramContentService
            .Setup(service => service.GetPostsAsync(
                It.Is<InstagramPostListRequest>(request =>
                    request.AccessToken == "ig-token" &&
                    request.InstagramUserId == "ig-user" &&
                    request.Limit == 5 &&
                    request.Cursor == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new InstagramPostPageResult(
                Posts:
                [
                    new InstagramPostDetails(
                        Id: "ig-post-1",
                        Caption: "Launch reel",
                        MediaType: "VIDEO",
                        MediaProductType: "REELS",
                        MediaUrl: "https://cdn.example.com/reel-1.mp4",
                        ThumbnailUrl: "https://cdn.example.com/reel-1.jpg",
                        Permalink: "https://instagram.com/p/ig-post-1",
                        Timestamp: "2026-03-18T09:00:00+0000",
                        Username: "creator",
                        LikeCount: 40,
                        CommentCount: 8),
                    new InstagramPostDetails(
                        Id: "ig-post-2",
                        Caption: "Carousel",
                        MediaType: "CAROUSEL_ALBUM",
                        MediaProductType: "FEED",
                        MediaUrl: "https://cdn.example.com/post-2.jpg",
                        ThumbnailUrl: "https://cdn.example.com/post-2-thumb.jpg",
                        Permalink: "https://instagram.com/p/ig-post-2",
                        Timestamp: "2026-03-17T09:00:00+0000",
                        Username: "creator",
                        LikeCount: 10,
                        CommentCount: 2)
                ],
                NextCursor: "next",
                HasMore: true)));

        instagramContentService
            .Setup(service => service.GetPostInsightsAsync(
                It.Is<InstagramPostInsightsRequest>(request =>
                    request.AccessToken == "ig-token" &&
                    request.PostId == "ig-post-1"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new InstagramPostInsights(
                Views: 300,
                Reach: 500,
                Impressions: 900,
                Saved: 4,
                Shares: 6)));

        instagramContentService
            .Setup(service => service.GetPostInsightsAsync(
                It.Is<InstagramPostInsightsRequest>(request =>
                    request.AccessToken == "ig-token" &&
                    request.PostId == "ig-post-2"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new InstagramPostInsights(
                Views: null,
                Reach: 120,
                Impressions: 200,
                Saved: 1,
                Shares: 2)));

        instagramContentService
            .Setup(service => service.GetAccountInsightsAsync(
                It.Is<InstagramAccountInsightsRequest>(request =>
                    request.AccessToken == "ig-token" &&
                    request.InstagramUserId == "ig-user"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new InstagramAccountInsights(
                Id: "ig-user",
                Name: "Instagram Creator",
                Username: "creator",
                Followers: 1000,
                Following: 150,
                MediaCount: 20,
                ProfilePictureUrl: "https://cdn.example.com/profile.jpg")));

        postMetricSnapshotRepository
            .Setup(repository => repository.GetLatestByPlatformPostIdsAsync(
                userId,
                socialMediaId,
                It.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(new[] { "ig-post-1", "ig-post-2" })),
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
        result.Value.Platform.Should().Be("instagram");
        result.Value.AggregatedStats.Views.Should().Be(420);
        result.Value.AggregatedStats.Reach.Should().Be(620);
        result.Value.AggregatedStats.Impressions.Should().Be(1100);
        result.Value.AggregatedStats.Likes.Should().Be(50);
        result.Value.AggregatedStats.Comments.Should().Be(10);
        result.Value.AggregatedStats.Shares.Should().Be(8);
        result.Value.AggregatedStats.Saves.Should().Be(5);
        result.Value.AggregatedStats.TotalInteractions.Should().Be(68);
        result.Value.AccountInsights!.Followers.Should().Be(1000);
    }

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
