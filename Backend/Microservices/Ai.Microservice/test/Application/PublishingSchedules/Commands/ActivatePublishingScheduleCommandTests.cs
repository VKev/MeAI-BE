using Application.Abstractions.Automation;
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

public sealed class ActivatePublishingScheduleCommandTests
{
    [Fact]
    public async Task Handle_ShouldReactivateAgenticSchedule_WhenExecuteAtUtcIsStillInFuture()
    {
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var scheduleId = Guid.NewGuid();
        var socialMediaId = Guid.NewGuid();

        var schedule = new PublishingSchedule
        {
            Id = scheduleId,
            UserId = userId,
            WorkspaceId = workspaceId,
            Name = "Future AI schedule",
            Mode = PublishingScheduleState.AgenticMode,
            Status = PublishingScheduleState.StatusCancelled,
            ExecuteAtUtc = DateTime.UtcNow.AddHours(2),
            Timezone = "Asia/Ho_Chi_Minh",
            IsPrivate = false,
            PlatformPreference = "facebook",
            AgentPrompt = "Hãy đăng bài tổng hợp tin AI mới nhất vào thời điểm chạy.",
            MaxContentLength = 280,
            SearchQueryTemplate = "tin nóng AI hôm nay",
            ExecutionContextJson = AgenticScheduleExecutionContextSerializer.Serialize(
                new AgenticScheduleExecutionContext(
                    Search: new PublishingScheduleSearchInput("tin nóng AI hôm nay", 5, "VN", "vi", "pd"))),
            Targets =
            [
                new PublishingScheduleTarget
                {
                    Id = Guid.NewGuid(),
                    ScheduleId = scheduleId,
                    SocialMediaId = socialMediaId,
                    Platform = "facebook",
                    TargetLabel = "facebook",
                    IsPrimary = true
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
                It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 0),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Post>());

        var workspaceRepository = new Mock<IWorkspaceRepository>(MockBehavior.Strict);
        workspaceRepository
            .Setup(repository => repository.ExistsForUserAsync(workspaceId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var postPublicationRepository = new Mock<IPostPublicationRepository>(MockBehavior.Strict);
        postPublicationRepository
            .Setup(repository => repository.GetByPostIdsAsync(
                It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 0),
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

        var n8nClient = new Mock<IN8nWorkflowClient>(MockBehavior.Strict);
        n8nClient
            .Setup(client => client.RegisterScheduledAgentJobAsync(
                It.Is<N8nScheduledAgentJobRequest>(request =>
                    request.ScheduleId == scheduleId &&
                    request.UserId == userId &&
                    request.WorkspaceId == workspaceId &&
                    request.Search.QueryTemplate == "tin nóng AI hôm nay" &&
                    request.Search.Count == 5),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new N8nScheduledAgentJobAck(
                "n8n-execution-reactivated",
                DateTime.UtcNow)));

        var handler = new ActivatePublishingScheduleCommandHandler(
            scheduleRepository.Object,
            postRepository.Object,
            workspaceRepository.Object,
            postPublicationRepository.Object,
            userSocialMediaService.Object,
            n8nClient.Object);

        var result = await handler.Handle(
            new ActivatePublishingScheduleCommand(scheduleId, userId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
        schedule.Status.Should().Be(PublishingScheduleState.StatusWaitingForExecution);
        schedule.ErrorCode.Should().BeNull();
        schedule.ErrorMessage.Should().BeNull();

        var executionContext = AgenticScheduleExecutionContextSerializer.Parse(schedule.ExecutionContextJson);
        executionContext.Search.Should().NotBeNull();
        executionContext.Search!.QueryTemplate.Should().Be("tin nóng AI hôm nay");
        executionContext.N8nJobId.Should().NotBeNull();
        executionContext.N8nExecutionId.Should().Be("n8n-execution-reactivated");

        scheduleRepository.VerifyAll();
        postRepository.VerifyAll();
        workspaceRepository.VerifyAll();
        postPublicationRepository.VerifyAll();
        userSocialMediaService.VerifyAll();
        n8nClient.VerifyAll();
    }

    [Fact]
    public async Task Handle_ShouldRejectAgenticReactivation_WhenExecuteAtUtcIsInPast()
    {
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var scheduleId = Guid.NewGuid();
        var socialMediaId = Guid.NewGuid();

        var schedule = new PublishingSchedule
        {
            Id = scheduleId,
            UserId = userId,
            WorkspaceId = workspaceId,
            Name = "Expired AI schedule",
            Mode = PublishingScheduleState.AgenticMode,
            Status = PublishingScheduleState.StatusCancelled,
            ExecuteAtUtc = DateTime.UtcNow.AddMinutes(-5),
            Timezone = "Asia/Ho_Chi_Minh",
            PlatformPreference = "facebook",
            AgentPrompt = "Hãy đăng bài tổng hợp tin AI mới nhất vào thời điểm chạy.",
            MaxContentLength = 280,
            SearchQueryTemplate = "tin nóng AI hôm nay",
            ExecutionContextJson = AgenticScheduleExecutionContextSerializer.Serialize(
                new AgenticScheduleExecutionContext(
                    Search: new PublishingScheduleSearchInput("tin nóng AI hôm nay", 5, "VN", "vi", "pd"))),
            Targets =
            [
                new PublishingScheduleTarget
                {
                    Id = Guid.NewGuid(),
                    ScheduleId = scheduleId,
                    SocialMediaId = socialMediaId,
                    Platform = "facebook",
                    TargetLabel = "facebook",
                    IsPrimary = true
                }
            ]
        };

        var scheduleRepository = new Mock<IPublishingScheduleRepository>(MockBehavior.Strict);
        scheduleRepository
            .Setup(repository => repository.GetByIdForUpdateAsync(scheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedule);

        var postRepository = new Mock<IPostRepository>(MockBehavior.Strict);

        var workspaceRepository = new Mock<IWorkspaceRepository>(MockBehavior.Strict);
        workspaceRepository
            .Setup(repository => repository.ExistsForUserAsync(workspaceId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var postPublicationRepository = new Mock<IPostPublicationRepository>(MockBehavior.Strict);

        var userSocialMediaService = new Mock<IUserSocialMediaService>(MockBehavior.Strict);

        var n8nClient = new Mock<IN8nWorkflowClient>(MockBehavior.Strict);

        var handler = new ActivatePublishingScheduleCommandHandler(
            scheduleRepository.Object,
            postRepository.Object,
            workspaceRepository.Object,
            postPublicationRepository.Object,
            userSocialMediaService.Object,
            n8nClient.Object);

        var result = await handler.Handle(
            new ActivatePublishingScheduleCommand(scheduleId, userId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(PublishingScheduleErrors.ExecuteAtInPast.Code);

        scheduleRepository.VerifyAll();
        postRepository.VerifyNoOtherCalls();
        workspaceRepository.VerifyAll();
        postPublicationRepository.VerifyNoOtherCalls();
        userSocialMediaService.VerifyAll();
        n8nClient.VerifyNoOtherCalls();
    }
}
