using Application.Abstractions.Resources;
using Application.Posts;
using Application.Posts.Commands;
using Application.Posts.Models;
using Application.PublishingSchedules;
using Domain.Entities;
using Domain.Repositories;
using FluentAssertions;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace AiMicroservice.Tests.Application.Posts.Commands;

public sealed class UpdatePostCommandTests
{
    [Fact]
    public async Task Handle_ShouldConvertScheduledPostBackToDraftAndClearSchedule()
    {
        var userId = Guid.NewGuid();
        var postId = Guid.NewGuid();
        var scheduleId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();

        var post = new Post
        {
            Id = postId,
            UserId = userId,
            WorkspaceId = workspaceId,
            Title = "Scheduled post",
            Content = new PostContent
            {
                Content = "Caption",
                PostType = "posts",
                ResourceList = []
            },
            Status = "scheduled",
            ScheduleGroupId = scheduleId,
            ScheduledAtUtc = DateTime.UtcNow.AddHours(1),
            ScheduleTimezone = "Asia/Ho_Chi_Minh",
            ScheduledSocialMediaIds = [Guid.NewGuid()],
            ScheduledIsPrivate = true
        };

        var schedule = new PublishingSchedule
        {
            Id = scheduleId,
            UserId = userId,
            WorkspaceId = workspaceId,
            Status = PublishingScheduleState.StatusScheduled,
            Items =
            [
                new PublishingScheduleItem
                {
                    Id = Guid.NewGuid(),
                    ScheduleId = scheduleId,
                    ItemType = PublishingScheduleState.ItemTypePost,
                    ItemId = postId,
                    Status = PublishingScheduleState.ItemStatusScheduled
                }
            ]
        };

        var postRepository = new Mock<IPostRepository>();
        postRepository
            .Setup(repository => repository.GetByIdForUpdateAsync(postId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(post);
        postRepository
            .Setup(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var publishingScheduleRepository = new Mock<IPublishingScheduleRepository>();
        publishingScheduleRepository
            .Setup(repository => repository.GetByIdForUpdateAsync(scheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedule);

        var handler = new UpdatePostCommandHandler(
            postRepository.Object,
            publishingScheduleRepository.Object,
            Mock.Of<IWorkspaceRepository>(),
            Mock.Of<IChatSessionRepository>(),
            CreatePostResponseBuilder());

        var result = await handler.Handle(
            new UpdatePostCommand(
                postId,
                userId,
                null,
                null,
                null,
                null,
                null,
                "draft"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("draft");
        result.Value.Schedule.Should().BeNull();

        post.Status.Should().Be("draft");
        post.ScheduleGroupId.Should().BeNull();
        post.ScheduledAtUtc.Should().BeNull();
        post.ScheduleTimezone.Should().BeNull();
        post.ScheduledSocialMediaIds.Should().BeEmpty();
        post.ScheduledIsPrivate.Should().BeNull();

        schedule.Status.Should().Be(PublishingScheduleState.StatusCancelled);
        schedule.Items.Single().Status.Should().Be(PublishingScheduleState.ItemStatusCancelled);

        postRepository.Verify(repository => repository.Update(post), Times.Once);
        publishingScheduleRepository.Verify(repository => repository.Update(schedule), Times.Once);
    }

    private static PostResponseBuilder CreatePostResponseBuilder()
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

        var postPublicationRepository = new Mock<IPostPublicationRepository>();
        postPublicationRepository
            .Setup(repository => repository.GetByPostIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PostPublication>());

        return new PostResponseBuilder(userResourceService.Object, postPublicationRepository.Object);
    }
}
