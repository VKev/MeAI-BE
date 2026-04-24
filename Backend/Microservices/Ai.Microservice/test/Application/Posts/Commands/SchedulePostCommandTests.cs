using Application.Abstractions.Resources;
using Application.Abstractions.SocialMedias;
using Application.Posts;
using Application.Posts.Commands;
using Application.Posts.Models;
using Domain.Entities;
using Domain.Repositories;
using FluentAssertions;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace AiMicroservice.Tests.Application.Posts.Commands;

public sealed class SchedulePostCommandTests
{
    [Fact]
    public async Task Handle_ShouldPersistScheduleAndReturnScheduledPost()
    {
        var userId = Guid.NewGuid();
        var postId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var socialMediaId = Guid.NewGuid();

        var post = new Post
        {
            Id = postId,
            UserId = userId,
            WorkspaceId = workspaceId,
            Content = new PostContent
            {
                Content = "Caption",
                PostType = "posts",
                ResourceList = []
            },
            Status = "draft"
        };

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
            .Setup(repository => repository.GetByPostIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PostPublication>());

        var userSocialMediaService = new Mock<IUserSocialMediaService>();
        userSocialMediaService
            .Setup(service => service.GetSocialMediasAsync(
                userId,
                It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == socialMediaId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<UserSocialMediaResult>>(
            [
                new UserSocialMediaResult(socialMediaId, "threads", "{}")
            ]));

        var handler = new SchedulePostCommandHandler(
            postRepository.Object,
            postPublicationRepository.Object,
            userSocialMediaService.Object,
            CreatePostResponseBuilder(postPublicationRepository.Object));

        var scheduledAtUtc = DateTime.UtcNow.AddMinutes(20);
        var result = await handler.Handle(
            new SchedulePostCommand(
                postId,
                userId,
                new PostScheduleInput(
                    null,
                    scheduledAtUtc,
                    "Asia/Bangkok",
                    [socialMediaId],
                    true)),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("scheduled");
        result.Value.Schedule.Should().NotBeNull();
        result.Value.Schedule!.ScheduledAtUtc.Should().BeCloseTo(scheduledAtUtc, TimeSpan.FromSeconds(1));
        result.Value.Schedule.Timezone.Should().Be("Asia/Bangkok");
        result.Value.Schedule.SocialMediaIds.Should().ContainSingle().Which.Should().Be(socialMediaId);
        result.Value.Schedule.IsPrivate.Should().BeTrue();

        post.Status.Should().Be("scheduled");
        post.ScheduledSocialMediaIds.Should().ContainSingle().Which.Should().Be(socialMediaId);
        post.ScheduleGroupId.Should().NotBeNull();
        postRepository.Verify(repository => repository.Update(post), Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldFailWhenScheduledTimeIsInPast()
    {
        var userId = Guid.NewGuid();
        var postId = Guid.NewGuid();

        var postRepository = new Mock<IPostRepository>();
        postRepository
            .Setup(repository => repository.GetByIdForUpdateAsync(postId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Post
            {
                Id = postId,
                UserId = userId,
                WorkspaceId = Guid.NewGuid(),
                Content = new PostContent
                {
                    Content = "Caption",
                    PostType = "posts",
                    ResourceList = []
                }
            });

        var handler = new SchedulePostCommandHandler(
            postRepository.Object,
            Mock.Of<IPostPublicationRepository>(),
            Mock.Of<IUserSocialMediaService>(),
            CreatePostResponseBuilder(Mock.Of<IPostPublicationRepository>()));

        var result = await handler.Handle(
            new SchedulePostCommand(
                postId,
                userId,
                new PostScheduleInput(
                    null,
                    DateTime.UtcNow.AddMinutes(-1),
                    "UTC",
                    [Guid.NewGuid()],
                    null)),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PostErrors.ScheduleInPast);
    }

    private static PostResponseBuilder CreatePostResponseBuilder(IPostPublicationRepository postPublicationRepository)
    {
        var userResourceService = new Mock<IUserResourceService>(MockBehavior.Strict);
        userResourceService
            .Setup(service => service.GetPublicUserProfilesByIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> userIds, CancellationToken _) =>
                Result.Success<IReadOnlyDictionary<Guid, PublicUserProfileResult>>(
                    userIds.ToDictionary(
                        id => id,
                        id => new PublicUserProfileResult(id, $"user-{id:N}", null, null))));

        return new PostResponseBuilder(userResourceService.Object, postPublicationRepository);
    }
}
