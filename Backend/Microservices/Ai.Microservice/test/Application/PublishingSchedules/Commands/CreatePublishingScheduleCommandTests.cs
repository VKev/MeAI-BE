using Application.Abstractions.SocialMedias;
using Application.PublishingSchedules;
using Application.PublishingSchedules.Commands;
using Application.PublishingSchedules.Models;
using Domain.Entities;
using Domain.Repositories;
using FluentAssertions;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace AiMicroservice.Tests.Application.PublishingSchedules.Commands;

public sealed class CreatePublishingScheduleCommandTests
{
    [Fact]
    public async Task Handle_ShouldCreateAggregateAndStampPosts()
    {
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var postId = Guid.NewGuid();
        var socialMediaId = Guid.NewGuid();
        var post = new Post
        {
            Id = postId,
            UserId = userId,
            WorkspaceId = workspaceId,
            Title = "Lottery summary",
            Content = new PostContent
            {
                Content = "Caption",
                PostType = "posts",
                ResourceList = []
            },
            Status = "draft"
        };

        var scheduleRepository = new Mock<IPublishingScheduleRepository>(MockBehavior.Strict);
        PublishingSchedule? storedSchedule = null;
        scheduleRepository
            .Setup(repository => repository.AddAsync(It.IsAny<PublishingSchedule>(), It.IsAny<CancellationToken>()))
            .Callback<PublishingSchedule, CancellationToken>((schedule, _) => storedSchedule = schedule)
            .Returns(Task.CompletedTask);
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
            .Setup(repository => repository.GetByIdsAsync(
                It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == postId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([post]);
        postRepository
            .Setup(repository => repository.Update(post));

        var workspaceRepository = new Mock<IWorkspaceRepository>(MockBehavior.Strict);
        workspaceRepository
            .Setup(repository => repository.ExistsForUserAsync(workspaceId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var postPublicationRepository = new Mock<IPostPublicationRepository>(MockBehavior.Strict);
        postPublicationRepository
            .Setup(repository => repository.GetByPostIdsAsync(
                It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == postId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PostPublication>());

        var userSocialMediaService = new Mock<IUserSocialMediaService>(MockBehavior.Strict);
        userSocialMediaService
            .Setup(service => service.GetSocialMediasAsync(
                userId,
                It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == socialMediaId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<UserSocialMediaResult>>(
            [
                new UserSocialMediaResult(socialMediaId, "facebook", "{}")
            ]));

        var handler = new CreatePublishingScheduleCommandHandler(
            scheduleRepository.Object,
            postRepository.Object,
            workspaceRepository.Object,
            postPublicationRepository.Object,
            userSocialMediaService.Object,
            new PublishingScheduleResponseBuilder(postRepository.Object));

        var scheduledAtUtc = DateTime.UtcNow.AddHours(2);
        var result = await handler.Handle(
            new CreatePublishingScheduleCommand(
                userId,
                workspaceId,
                "Daily lottery",
                "fixed_content",
                scheduledAtUtc,
                "Asia/Ho_Chi_Minh",
                false,
                [new PublishingScheduleItemInput("post", postId, 1, null)],
                [new PublishingScheduleTargetInput(socialMediaId, true)]),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        storedSchedule.Should().NotBeNull();
        storedSchedule!.Status.Should().Be(PublishingScheduleState.StatusScheduled);
        storedSchedule.Items.Should().ContainSingle(item =>
            item.ItemId == postId &&
            item.Status == PublishingScheduleState.ItemStatusScheduled);
        storedSchedule.Targets.Should().ContainSingle(target =>
            target.SocialMediaId == socialMediaId &&
            target.Platform == "facebook");

        post.ScheduleGroupId.Should().Be(storedSchedule.Id);
        post.ScheduledAtUtc.Should().BeCloseTo(scheduledAtUtc, TimeSpan.FromSeconds(1));
        post.ScheduleTimezone.Should().Be("Asia/Ho_Chi_Minh");
        post.ScheduledSocialMediaIds.Should().ContainSingle().Which.Should().Be(socialMediaId);
        post.Status.Should().Be("scheduled");

        result.Value.Items.Should().ContainSingle(item => item.ItemId == postId);
        result.Value.Targets.Should().ContainSingle(target => target.SocialMediaId == socialMediaId);

        scheduleRepository.VerifyAll();
        postRepository.VerifyAll();
        workspaceRepository.VerifyAll();
        postPublicationRepository.VerifyAll();
        userSocialMediaService.VerifyAll();
    }
}
