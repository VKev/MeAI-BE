using Application.PublishingSchedules;
using Application.PublishingSchedules.Commands;
using Domain.Entities;
using Domain.Repositories;
using FluentAssertions;
using Moq;

namespace AiMicroservice.Tests.Application.PublishingSchedules.Commands;

public sealed class CancelPublishingScheduleCommandTests
{
    [Fact]
    public async Task Handle_ShouldCancelScheduleAndClearPostScheduling()
    {
        var userId = Guid.NewGuid();
        var scheduleId = Guid.NewGuid();
        var postId = Guid.NewGuid();
        var post = new Post
        {
            Id = postId,
            UserId = userId,
            WorkspaceId = Guid.NewGuid(),
            ScheduleGroupId = scheduleId,
            ScheduledAtUtc = DateTime.UtcNow.AddHours(1),
            ScheduledSocialMediaIds = [Guid.NewGuid()],
            Status = "scheduled"
        };

        var schedule = new PublishingSchedule
        {
            Id = scheduleId,
            UserId = userId,
            Status = PublishingScheduleState.StatusScheduled,
            Items =
            [
                new PublishingScheduleItem
                {
                    Id = Guid.NewGuid(),
                    ScheduleId = scheduleId,
                    ItemId = postId,
                    ItemType = PublishingScheduleState.ItemTypePost,
                    Status = PublishingScheduleState.ItemStatusScheduled
                }
            ]
        };

        var scheduleRepository = new Mock<IPublishingScheduleRepository>(MockBehavior.Strict);
        scheduleRepository
            .Setup(repository => repository.GetByIdForUpdateAsync(scheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedule);
        scheduleRepository
            .Setup(repository => repository.Update(schedule));
        scheduleRepository
            .Setup(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var postRepository = new Mock<IPostRepository>(MockBehavior.Strict);
        postRepository
            .Setup(repository => repository.GetByIdsForUpdateAsync(
                It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == postId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([post]);
        postRepository
            .Setup(repository => repository.Update(post));

        var handler = new CancelPublishingScheduleCommandHandler(
            scheduleRepository.Object,
            postRepository.Object);

        var result = await handler.Handle(
            new CancelPublishingScheduleCommand(scheduleId, userId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
        schedule.Status.Should().Be(PublishingScheduleState.StatusCancelled);
        schedule.Items.Single().Status.Should().Be(PublishingScheduleState.ItemStatusCancelled);
        post.ScheduleGroupId.Should().BeNull();
        post.ScheduledAtUtc.Should().BeNull();
        post.ScheduledSocialMediaIds.Should().BeEmpty();

        scheduleRepository.VerifyAll();
        postRepository.VerifyAll();
    }
}
