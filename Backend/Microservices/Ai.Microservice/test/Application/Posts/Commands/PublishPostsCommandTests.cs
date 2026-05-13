using Application.Abstractions.SocialMedias;
using Application.Abstractions.Feed;
using Application.Posts.Commands;
using Domain.Entities;
using Domain.Repositories;
using FluentAssertions;
using MassTransit;
using Moq;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Contracts.Publishing;

namespace AiMicroservice.Tests.Application.Posts.Commands;

public sealed class PublishPostsCommandTests
{
    [Fact]
    public async Task Handle_ShouldCreateProcessingPlaceholdersAndPublishTargetMessages()
    {
        var userId = Guid.NewGuid();
        var firstPostId = Guid.NewGuid();
        var secondPostId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var facebookId = Guid.NewGuid();
        var threadsId = Guid.NewGuid();
        var instagramId = Guid.NewGuid();
        var scheduleGroupId = Guid.NewGuid();

        var firstPost = CreatePost(firstPostId, userId, workspaceId);
        firstPost.ScheduleGroupId = scheduleGroupId;
        firstPost.ScheduledAtUtc = DateTime.UtcNow.AddHours(1);
        firstPost.ScheduledSocialMediaIds = [facebookId, threadsId];

        var secondPost = CreatePost(secondPostId, userId, workspaceId);

        var postRepository = new Mock<IPostRepository>();
        postRepository
            .Setup(repository => repository.GetByIdForUpdateAsync(firstPostId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstPost);
        postRepository
            .Setup(repository => repository.GetByIdForUpdateAsync(secondPostId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(secondPost);
        postRepository
            .Setup(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var postPublicationRepository = new Mock<IPostPublicationRepository>();
        postPublicationRepository
            .Setup(repository => repository.GetByPostIdForUpdateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PostPublication>());
        postPublicationRepository
            .Setup(repository => repository.AddRangeAsync(It.IsAny<IEnumerable<PostPublication>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var userSocialMediaService = new Mock<IUserSocialMediaService>();
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
                new UserSocialMediaResult(facebookId, "facebook", "{}"),
                new UserSocialMediaResult(threadsId, "threads", "{}"),
                new UserSocialMediaResult(instagramId, "instagram", "{}")
            ]));

        var feedPostPublishService = new Mock<IFeedPostPublishService>();
        feedPostPublishService
            .Setup(service => service.PublishAiPostToFeedAsync(It.IsAny<FeedDirectPublishRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new FeedDirectPublishResult(Guid.NewGuid(), DateTime.UtcNow)));

        var publishedMessages = new List<PublishToTargetRequested>();
        var bus = new Mock<IBus>();
        bus
            .Setup(instance => instance.Publish(It.IsAny<PublishToTargetRequested>(), It.IsAny<CancellationToken>()))
            .Callback<PublishToTargetRequested, CancellationToken>((message, _) => publishedMessages.Add(message))
            .Returns(Task.CompletedTask);

        var handler = new PublishPostsCommandHandler(
            postRepository.Object,
            postPublicationRepository.Object,
            userSocialMediaService.Object,
            feedPostPublishService.Object,
            bus.Object);

        var result = await handler.Handle(
            new PublishPostsCommand(
                userId,
                [
                    new PublishPostTargetInput(firstPostId, [facebookId, threadsId], true),
                    new PublishPostTargetInput(secondPostId, [instagramId])
                ]),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Posts.Should().HaveCount(2);
        result.Value.Posts.Should().OnlyContain(post => post.Status == "processing");
        result.Value.Posts.SelectMany(post => post.Results).Should().HaveCount(3);
        result.Value.Posts.SelectMany(post => post.Results).Should().OnlyContain(resultItem =>
            resultItem.PublishStatus == "processing" &&
            resultItem.PublicationId.HasValue);

        firstPost.ScheduleGroupId.Should().BeNull();
        firstPost.ScheduledAtUtc.Should().BeNull();
        firstPost.ScheduledSocialMediaIds.Should().BeEmpty();

        postPublicationRepository.Verify(repository => repository.AddRangeAsync(
            It.Is<IEnumerable<PostPublication>>(items => items.Count() == 2),
            It.IsAny<CancellationToken>()), Times.Once);
        postPublicationRepository.Verify(repository => repository.AddRangeAsync(
            It.Is<IEnumerable<PostPublication>>(items => items.Count() == 1),
            It.IsAny<CancellationToken>()), Times.Once);
        postRepository.Verify(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        publishedMessages.Should().HaveCount(3);
        publishedMessages.Should().Contain(message =>
            message.PostId == firstPostId &&
            message.SocialMediaId == facebookId &&
            message.IsPrivate == true &&
            message.PublishingScheduleId == scheduleGroupId);
        publishedMessages.Should().Contain(message =>
            message.PostId == firstPostId &&
            message.SocialMediaId == threadsId &&
            message.IsPrivate == true &&
            message.PublishingScheduleId == scheduleGroupId);
        publishedMessages.Should().Contain(message =>
            message.PostId == secondPostId &&
            message.SocialMediaId == instagramId &&
            message.IsPrivate == null &&
            message.PublishingScheduleId == null);
    }

    [Fact]
    public async Task Handle_ShouldPublishToMeAiFeedOnlyAndCreateFeedPublication()
    {
        var userId = Guid.NewGuid();
        var postId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var feedPostId = Guid.NewGuid();

        var post = CreatePost(postId, userId, workspaceId);

        var postRepository = new Mock<IPostRepository>();
        postRepository
            .Setup(repository => repository.GetByIdForUpdateAsync(postId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(post);
        postRepository
            .Setup(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var postPublicationRepository = new Mock<IPostPublicationRepository>();
        postPublicationRepository
            .Setup(repository => repository.GetByPostIdForUpdateAsync(postId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PostPublication>());
        postPublicationRepository
            .Setup(repository => repository.AddRangeAsync(
                It.Is<IEnumerable<PostPublication>>(publications =>
                    publications.Count() == 1 &&
                    publications.Single().PostId == postId &&
                    publications.Single().SocialMediaId == Guid.Empty &&
                    publications.Single().SocialMediaType == "meai_feed" &&
                    publications.Single().DestinationOwnerId == "meai_feed" &&
                    publications.Single().ExternalContentId == feedPostId.ToString() &&
                    publications.Single().PublishStatus == "published"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var userSocialMediaService = new Mock<IUserSocialMediaService>();
        var feedPostPublishService = new Mock<IFeedPostPublishService>();
        feedPostPublishService
            .Setup(service => service.PublishAiPostToFeedAsync(
                It.Is<FeedDirectPublishRequest>(request =>
                    request.UserId == userId &&
                    request.WorkspaceId == workspaceId &&
                    request.SourceAiPostId == postId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new FeedDirectPublishResult(feedPostId, DateTime.UtcNow)));

        var bus = new Mock<IBus>();

        var handler = new PublishPostsCommandHandler(
            postRepository.Object,
            postPublicationRepository.Object,
            userSocialMediaService.Object,
            feedPostPublishService.Object,
            bus.Object);

        var result = await handler.Handle(
            new PublishPostsCommand(
                userId,
                [new PublishPostTargetInput(postId, [], null, null, true)]),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Posts.Should().ContainSingle();
        result.Value.Posts[0].Status.Should().Be("published");
        result.Value.Posts[0].Results.Should().ContainSingle();
        result.Value.Posts[0].Results[0].SocialMediaId.Should().BeNull();
        result.Value.Posts[0].Results[0].SocialMediaType.Should().Be("meai_feed");
        result.Value.Posts[0].Results[0].InternalTargetKey.Should().Be("meai_feed");
        result.Value.Posts[0].Results[0].ExternalPostId.Should().Be(feedPostId.ToString());
        result.Value.Posts[0].Results[0].PublishStatus.Should().Be("published");

        postPublicationRepository.Verify(
            repository => repository.AddRangeAsync(It.IsAny<IEnumerable<PostPublication>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        bus.Verify(instance => instance.Publish(It.IsAny<PublishToTargetRequested>(), It.IsAny<CancellationToken>()), Times.Never);
        userSocialMediaService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_ShouldFailWhenTargetsAreMissing()
    {
        var postRepository = new Mock<IPostRepository>();
        var postPublicationRepository = new Mock<IPostPublicationRepository>();
        var userSocialMediaService = new Mock<IUserSocialMediaService>();
        var feedPostPublishService = new Mock<IFeedPostPublishService>();
        var bus = new Mock<IBus>();

        var handler = new PublishPostsCommandHandler(
            postRepository.Object,
            postPublicationRepository.Object,
            userSocialMediaService.Object,
            feedPostPublishService.Object,
            bus.Object);

        var result = await handler.Handle(
            new PublishPostsCommand(Guid.NewGuid(), []),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Post.PublishMissingTargets");
        userSocialMediaService.VerifyNoOtherCalls();
        postRepository.VerifyNoOtherCalls();
    }

    private static Post CreatePost(Guid postId, Guid userId, Guid workspaceId) =>
        new()
        {
            Id = postId,
            UserId = userId,
            WorkspaceId = workspaceId,
            Content = new PostContent
            {
                Content = "Caption",
                PostType = "posts",
                ResourceList = [Guid.NewGuid().ToString()]
            },
            Status = "draft"
        };
}
