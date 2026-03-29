using Application.Abstractions.Facebook;
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

public sealed class FacebookPlatformPostQueriesTests
{
    [Fact]
    public async Task GetPlatformPosts_ShouldReturnFacebookPosts()
    {
        var userId = Guid.NewGuid();
        var socialMediaId = Guid.NewGuid();

        var facebookContentService = new Mock<IFacebookContentService>();
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
                        """{"access_token":"user-token"}""")
                }));

        facebookContentService
            .Setup(service => service.GetPostsAsync(
                It.Is<FacebookPostListRequest>(request =>
                    request.UserAccessToken == "user-token" &&
                    request.Limit == 10 &&
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
                        FullPictureUrl: "https://cdn.example.com/full.jpg",
                        MediaType: "image",
                        MediaUrl: "https://cdn.example.com/media.jpg",
                        ThumbnailUrl: "https://cdn.example.com/thumb.jpg",
                        AttachmentTitle: "Campaign launch",
                        AttachmentDescription: "Description",
                        ReactionCount: 25,
                        CommentCount: 7,
                        ShareCount: 3)
                ],
                NextCursor: "next",
                HasMore: true)));

        postMetricSnapshotRepository
            .Setup(repository => repository.GetLatestByPlatformPostIdsAsync(
                userId,
                socialMediaId,
                It.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(new[] { "123_456" })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PostMetricSnapshot>());

        var handler = new GetSocialMediaPlatformPostsQueryHandler(
            facebookContentService.Object,
            userSocialMediaService.Object,
            tikTokContentService.Object,
            threadsContentService.Object,
            postMetricSnapshotRepository.Object);

        var result = await handler.Handle(
            new GetSocialMediaPlatformPostsQuery(userId, socialMediaId, null, 10),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Platform.Should().Be("facebook");
        result.Value.NextCursor.Should().Be("next");
        result.Value.HasMore.Should().BeTrue();
        result.Value.Items.Should().ContainSingle();

        var item = result.Value.Items[0];
        item.PlatformPostId.Should().Be("123_456");
        item.Title.Should().Be("Campaign launch");
        item.Text.Should().Be("Launch update");
        item.MediaType.Should().Be("image");
        item.Permalink.Should().Be("https://facebook.com/123_456");
        item.Stats.Should().BeEquivalentTo(new SocialPlatformPostStatsResponse(
            Views: null,
            Likes: 25,
            Comments: 7,
            Replies: null,
            Shares: 3,
            Reposts: null,
            Quotes: null,
            TotalInteractions: 35));
    }

    [Fact]
    public async Task GetPlatformPostAnalytics_ShouldFetchAndCacheFacebookMetrics()
    {
        var userId = Guid.NewGuid();
        var socialMediaId = Guid.NewGuid();
        var postId = "123_456";

        var facebookContentService = new Mock<IFacebookContentService>();
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
                        """{"access_token":"user-token"}""")
                }));

        postMetricSnapshotRepository
            .Setup(repository => repository.GetLatestAsync(
                userId,
                socialMediaId,
                postId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PostMetricSnapshot?)null);

        facebookContentService
            .Setup(service => service.GetPostAsync(
                It.Is<FacebookPostDetailsRequest>(request =>
                    request.UserAccessToken == "user-token" &&
                    request.PostId == postId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new FacebookPostDetails(
                Id: postId,
                PageId: "123",
                Message: "Launch update",
                Story: null,
                PermalinkUrl: "https://facebook.com/123_456",
                CreatedTime: "2026-03-18T09:00:00+0000",
                FullPictureUrl: "https://cdn.example.com/full.jpg",
                MediaType: "image",
                MediaUrl: "https://cdn.example.com/media.jpg",
                ThumbnailUrl: "https://cdn.example.com/thumb.jpg",
                AttachmentTitle: "Campaign launch",
                AttachmentDescription: "Description",
                ReactionCount: 25,
                CommentCount: 7,
                ShareCount: 3)));

        postMetricSnapshotRepository
            .Setup(repository => repository.GetLatestForUpdateAsync(
                userId,
                socialMediaId,
                postId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PostMetricSnapshot?)null);

        postMetricSnapshotRepository
            .Setup(repository => repository.AddAsync(
                It.Is<PostMetricSnapshot>(metric =>
                    metric.UserId == userId &&
                    metric.SocialMediaId == socialMediaId &&
                    metric.Platform == "facebook" &&
                    metric.PlatformPostId == postId &&
                    metric.LikeCount == 25 &&
                    metric.CommentCount == 7 &&
                    metric.ShareCount == 3),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        postMetricSnapshotRepository
            .Setup(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = new GetSocialMediaPlatformPostAnalyticsQueryHandler(
            facebookContentService.Object,
            userSocialMediaService.Object,
            tikTokContentService.Object,
            threadsContentService.Object,
            postMetricSnapshotRepository.Object);

        var result = await handler.Handle(
            new GetSocialMediaPlatformPostAnalyticsQuery(userId, socialMediaId, postId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Platform.Should().Be("facebook");
        result.Value.PlatformPostId.Should().Be(postId);
        result.Value.Stats.Should().BeEquivalentTo(new SocialPlatformPostStatsResponse(
            Views: null,
            Likes: 25,
            Comments: 7,
            Replies: null,
            Shares: 3,
            Reposts: null,
            Quotes: null,
            TotalInteractions: 35));

        postMetricSnapshotRepository.Verify(repository => repository.AddAsync(
            It.IsAny<PostMetricSnapshot>(),
            It.IsAny<CancellationToken>()), Times.Once);
        postMetricSnapshotRepository.Verify(repository => repository.SaveChangesAsync(
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
