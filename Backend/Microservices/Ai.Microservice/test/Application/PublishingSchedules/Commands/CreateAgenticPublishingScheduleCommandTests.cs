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

public sealed class CreateAgenticPublishingScheduleCommandTests
{
    [Fact]
    public async Task Handle_ShouldCreateAgenticScheduleAndRegisterN8nJob()
    {
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var socialMediaId = Guid.NewGuid();
        var executionId = "n8n-execution-123";

        var scheduleRepository = new Mock<IPublishingScheduleRepository>(MockBehavior.Strict);
        PublishingSchedule? storedSchedule = null;
        scheduleRepository
            .Setup(repository => repository.AddAsync(It.IsAny<PublishingSchedule>(), It.IsAny<CancellationToken>()))
            .Callback<PublishingSchedule, CancellationToken>((schedule, _) => storedSchedule = schedule)
            .Returns(Task.CompletedTask);
        scheduleRepository
            .Setup(repository => repository.Update(It.IsAny<PublishingSchedule>()));
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
                    request.UserId == userId &&
                    request.WorkspaceId == workspaceId &&
                    request.Search.QueryTemplate == "kết quả xổ số miền bắc hôm nay" &&
                    request.Search.Count == 5),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new N8nScheduledAgentJobAck(
                executionId,
                DateTime.UtcNow)));

        var handler = new CreateAgenticPublishingScheduleCommandHandler(
            scheduleRepository.Object,
            workspaceRepository.Object,
            postRepository.Object,
            postPublicationRepository.Object,
            userSocialMediaService.Object,
            new PublishingScheduleResponseBuilder(postRepository.Object),
            n8nClient.Object);

        var executeAtUtc = DateTime.UtcNow.AddHours(2);
        var result = await handler.Handle(
            new CreateAgenticPublishingScheduleCommand(
                userId,
                workspaceId,
                "Lottery runtime",
                "agentic",
                executeAtUtc,
                "Asia/Ho_Chi_Minh",
                false,
                "facebook",
                "Vào 5h chiều hãy tra kết quả xổ số miền bắc rồi đăng nó lên Facebook.",
                280,
                new PublishingScheduleSearchInput(
                    "kết quả xổ số miền bắc hôm nay",
                    5,
                    "VN",
                    "vi",
                    "pd"),
                [new PublishingScheduleTargetInput(socialMediaId, true)]),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        storedSchedule.Should().NotBeNull();
        storedSchedule!.Mode.Should().Be(PublishingScheduleState.AgenticMode);
        storedSchedule.Status.Should().Be(PublishingScheduleState.StatusWaitingForExecution);
        storedSchedule.Items.Should().BeEmpty();
        storedSchedule.PlatformPreference.Should().Be("facebook");
        storedSchedule.MaxContentLength.Should().Be(280);
        storedSchedule.SearchQueryTemplate.Should().Be("kết quả xổ số miền bắc hôm nay");
        storedSchedule.Targets.Should().ContainSingle(target => target.SocialMediaId == socialMediaId);

        var executionContext = AgenticScheduleExecutionContextSerializer.Parse(storedSchedule.ExecutionContextJson);
        executionContext.Search.Should().NotBeNull();
        executionContext.Search!.QueryTemplate.Should().Be("kết quả xổ số miền bắc hôm nay");
        executionContext.N8nJobId.Should().NotBeNull();
        executionContext.N8nExecutionId.Should().Be(executionId);

        result.Value.Mode.Should().Be(PublishingScheduleState.AgenticMode);
        result.Value.Status.Should().Be(PublishingScheduleState.StatusWaitingForExecution);
        result.Value.PlatformPreference.Should().Be("facebook");
        result.Value.MaxContentLength.Should().Be(280);
        result.Value.Search.Should().NotBeNull();
        result.Value.Search!.QueryTemplate.Should().Be("kết quả xổ số miền bắc hôm nay");
        result.Value.Targets.Should().ContainSingle(target => target.SocialMediaId == socialMediaId);

        scheduleRepository.VerifyAll();
        postRepository.VerifyAll();
        workspaceRepository.VerifyAll();
        postPublicationRepository.VerifyAll();
        userSocialMediaService.VerifyAll();
        n8nClient.VerifyAll();
    }
}
