using Application.Abstractions.Facebook;
using Application.Abstractions.Instagram;
using Application.Abstractions.Resources;
using Application.Abstractions.SocialMedias;
using Application.Abstractions.Threads;
using Application.Abstractions.TikTok;
using Application.Posts.Commands;
using Domain.Entities;
using Domain.Repositories;
using FluentAssertions;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace AiMicroservice.Tests.Application.Posts.Commands;

public sealed class PublishPostsCommandTests
{
    [Fact]
    public async Task Handle_ShouldPublishMultiplePostsAndSocialMediaTargets()
    {
        var userId = Guid.NewGuid();
        var firstPostId = Guid.NewGuid();
        var secondPostId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var firstResourceId = Guid.NewGuid();
        var secondResourceId = Guid.NewGuid();
        var facebookId = Guid.NewGuid();
        var threadsId = Guid.NewGuid();
        var instagramId = Guid.NewGuid();

        var postRepository = new Mock<IPostRepository>();
        var postPublicationRepository = new Mock<IPostPublicationRepository>();
        var userResourceService = new Mock<IUserResourceService>();
        var userSocialMediaService = new Mock<IUserSocialMediaService>();
        var facebookPublishService = new Mock<IFacebookPublishService>();
        var instagramPublishService = new Mock<IInstagramPublishService>();
        var tikTokPublishService = new Mock<ITikTokPublishService>();
        var threadsPublishService = new Mock<IThreadsPublishService>();

        postRepository
            .Setup(repository => repository.GetByIdForUpdateAsync(firstPostId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Post
            {
                Id = firstPostId,
                UserId = userId,
                WorkspaceId = workspaceId,
                Content = new PostContent
                {
                    Content = "First caption",
                    PostType = "posts",
                    ResourceList = [firstResourceId.ToString()]
                },
                Status = "draft"
            });

        postRepository
            .Setup(repository => repository.GetByIdForUpdateAsync(secondPostId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Post
            {
                Id = secondPostId,
                UserId = userId,
                WorkspaceId = workspaceId,
                Content = new PostContent
                {
                    Content = "Second caption",
                    PostType = "posts",
                    ResourceList = [secondResourceId.ToString()]
                },
                Status = "draft"
            });

        userSocialMediaService
            .Setup(service => service.GetSocialMediasAsync(
                userId,
                It.Is<IReadOnlyList<Guid>>(ids =>
                    ids.Count == 3 &&
                    ids.Contains(facebookId) &&
                    ids.Contains(threadsId) &&
                    ids.Contains(instagramId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<UserSocialMediaResult>>(
            [
                new UserSocialMediaResult(
                    facebookId,
                    "facebook",
                    "{\"user_access_token\":\"facebook-token\",\"page_id\":\"page-1\",\"page_access_token\":\"page-token\"}"),
                new UserSocialMediaResult(
                    threadsId,
                    "threads",
                    "{\"access_token\":\"threads-token\",\"user_id\":\"threads-user\"}"),
                new UserSocialMediaResult(
                    instagramId,
                    "instagram",
                    "{\"access_token\":\"instagram-token\",\"instagram_business_account_id\":\"ig-user\"}")
            ]));

        userResourceService
            .Setup(service => service.GetPresignedResourcesAsync(
                userId,
                It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == firstResourceId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<UserResourcePresignResult>>(
            [
                new UserResourcePresignResult(
                    firstResourceId,
                    "https://cdn.example.com/first.png",
                    "image/png",
                    "image")
            ]));

        userResourceService
            .Setup(service => service.GetPresignedResourcesAsync(
                userId,
                It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == secondResourceId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<UserResourcePresignResult>>(
            [
                new UserResourcePresignResult(
                    secondResourceId,
                    "https://cdn.example.com/second.png",
                    "image/png",
                    "image")
            ]));

        facebookPublishService
            .Setup(service => service.PublishAsync(
                It.IsAny<FacebookPublishRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<FacebookPublishResult>>(
            [
                new FacebookPublishResult("page-1", "facebook-post-1")
            ]));

        threadsPublishService
            .Setup(service => service.PublishAsync(
                It.IsAny<ThreadsPublishRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new ThreadsPublishResult("threads-user", "threads-post-1")));

        instagramPublishService
            .Setup(service => service.PublishAsync(
                It.IsAny<InstagramPublishRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new InstagramPublishResult("ig-user", "instagram-post-1")));

        postPublicationRepository
            .Setup(repository => repository.AddRangeAsync(It.IsAny<IEnumerable<PostPublication>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        postRepository
            .Setup(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = new PublishPostsCommandHandler(
            postRepository.Object,
            postPublicationRepository.Object,
            userResourceService.Object,
            userSocialMediaService.Object,
            facebookPublishService.Object,
            instagramPublishService.Object,
            tikTokPublishService.Object,
            threadsPublishService.Object);

        var result = await handler.Handle(
            new PublishPostsCommand(
                userId,
                [
                    new PublishPostTargetInput(firstPostId, [facebookId, threadsId]),
                    new PublishPostTargetInput(secondPostId, [instagramId])
                ]),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Posts.Should().HaveCount(2);
        result.Value.Posts.Should().OnlyContain(post => post.Status == "published");
        result.Value.Posts.Should().Contain(post =>
            post.PostId == firstPostId &&
            post.Results.Count == 2 &&
            post.Results.Any(item => item.SocialMediaId == facebookId && item.ExternalPostId == "facebook-post-1") &&
            post.Results.Any(item => item.SocialMediaId == threadsId && item.ExternalPostId == "threads-post-1"));
        result.Value.Posts.Should().Contain(post =>
            post.PostId == secondPostId &&
            post.Results.Count == 1 &&
            post.Results[0].SocialMediaId == instagramId &&
            post.Results[0].ExternalPostId == "instagram-post-1");

        facebookPublishService.Verify(service => service.PublishAsync(
            It.Is<FacebookPublishRequest>(request =>
                request.Message == "First caption" &&
                request.Media.Count == 1 &&
                request.Media[0].Url == "https://cdn.example.com/first.png"),
            It.IsAny<CancellationToken>()), Times.Once);

        threadsPublishService.Verify(service => service.PublishAsync(
            It.Is<ThreadsPublishRequest>(request =>
                request.Text == "First caption" &&
                request.Media != null &&
                request.Media.Url == "https://cdn.example.com/first.png"),
            It.IsAny<CancellationToken>()), Times.Once);

        instagramPublishService.Verify(service => service.PublishAsync(
            It.Is<InstagramPublishRequest>(request =>
                request.Caption == "Second caption" &&
                request.Media.Url == "https://cdn.example.com/second.png"),
            It.IsAny<CancellationToken>()), Times.Once);

        postPublicationRepository.Verify(repository => repository.AddRangeAsync(
            It.IsAny<IEnumerable<PostPublication>>(),
            It.IsAny<CancellationToken>()), Times.Exactly(3));

        postRepository.Verify(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Handle_ShouldFailWhenTargetsAreMissing()
    {
        var postRepository = new Mock<IPostRepository>();
        var postPublicationRepository = new Mock<IPostPublicationRepository>();
        var userResourceService = new Mock<IUserResourceService>();
        var userSocialMediaService = new Mock<IUserSocialMediaService>();
        var facebookPublishService = new Mock<IFacebookPublishService>();
        var instagramPublishService = new Mock<IInstagramPublishService>();
        var tikTokPublishService = new Mock<ITikTokPublishService>();
        var threadsPublishService = new Mock<IThreadsPublishService>();

        var handler = new PublishPostsCommandHandler(
            postRepository.Object,
            postPublicationRepository.Object,
            userResourceService.Object,
            userSocialMediaService.Object,
            facebookPublishService.Object,
            instagramPublishService.Object,
            tikTokPublishService.Object,
            threadsPublishService.Object);

        var result = await handler.Handle(
            new PublishPostsCommand(Guid.NewGuid(), []),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Post.PublishMissingTargets");
        userSocialMediaService.VerifyNoOtherCalls();
        postRepository.VerifyNoOtherCalls();
    }
}
