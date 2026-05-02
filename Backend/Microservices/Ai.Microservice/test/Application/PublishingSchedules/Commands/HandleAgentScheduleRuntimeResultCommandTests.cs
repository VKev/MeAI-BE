using Application.Abstractions.Automation;
using Application.Posts.Commands;
using Application.Posts.Models;
using Application.PublishingSchedules;
using Application.PublishingSchedules.Commands;
using Application.PublishingSchedules.Models;
using Domain.Entities;
using Domain.Repositories;
using FluentAssertions;
using MediatR;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace AiMicroservice.Tests.Application.PublishingSchedules.Commands;

public sealed class HandleAgentScheduleRuntimeResultCommandTests
{
    [Fact]
    public async Task Handle_ShouldCreateRuntimePostAndPublishIt()
    {
        var scheduleId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var socialMediaId = Guid.NewGuid();
        var runtimePostId = Guid.NewGuid();
        var callbackJobId = Guid.NewGuid();

        var schedule = new PublishingSchedule
        {
            Id = scheduleId,
            UserId = userId,
            WorkspaceId = workspaceId,
            Name = "Lottery runtime",
            Mode = PublishingScheduleState.AgenticMode,
            Status = PublishingScheduleState.StatusWaitingForExecution,
            Timezone = "Asia/Ho_Chi_Minh",
            ExecuteAtUtc = DateTime.UtcNow.AddHours(1),
            PlatformPreference = "facebook",
            AgentPrompt = "Đăng kết quả xổ số miền bắc lên Facebook.",
            ExecutionContextJson = AgenticScheduleExecutionContextSerializer.Serialize(
                new AgenticScheduleExecutionContext(
                    Search: new PublishingScheduleSearchInput(
                        "kết quả xổ số miền bắc hôm nay",
                        5,
                        "VN",
                        "vi",
                        "pd"),
                    N8nJobId: callbackJobId)),
            Targets =
            [
                new PublishingScheduleTarget
                {
                    Id = Guid.NewGuid(),
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

        var runtimeContentService = new Mock<IAgenticRuntimeContentService>(MockBehavior.Strict);
        var webSearchEnrichmentService = new Mock<IWebSearchEnrichmentService>(MockBehavior.Strict);
        webSearchEnrichmentService
            .Setup(service => service.EnrichAsync(
                It.Is<N8nWebSearchResponse>(search =>
                    search.Query == "kết quả xổ số miền bắc hôm nay" &&
                    search.Results.Count == 1),
                userId,
                workspaceId,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new N8nWebSearchResponse(
                "kết quả xổ số miền bắc hôm nay",
                DateTime.UtcNow,
                [
                    new N8nWebSearchResultItem(
                        "KQXS",
                        "https://example.com",
                        "Mô tả",
                        "example.com",
                        "KQXS Mien Bac",
                        "Chi tiet noi dung trang",
                        ["https://example.com/image.jpg"])
                ],
                "context",
                [
                    new N8nImportedResourceItem(
                        Guid.NewGuid(),
                        "https://cdn.example.com/image.jpg",
                        "image/jpeg",
                        "image",
                        "https://example.com/image.jpg",
                        "https://example.com")
                ]));
        runtimeContentService
            .Setup(service => service.GeneratePostDraftAsync(
                It.Is<AgenticRuntimeContentRequest>(request =>
                    request.ScheduleId == scheduleId &&
                    request.PlatformPreference == "facebook" &&
                    request.Search.Query == "kết quả xổ số miền bắc hôm nay"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AgenticRuntimePostDraft(
                "KQXS Miền Bắc",
                "Kết quả xổ số miền Bắc hôm nay: 12345",
                "#xoso #mienbac",
                "posts")));

        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        mediator
            .Setup(m => m.Send(
                It.Is<CreatePostCommand>(command =>
                    command.UserId == userId &&
                    command.WorkspaceId == workspaceId &&
                    command.Platform == "facebook" &&
                    command.NewPostBuilderOrigin == PostBuilderOriginKinds.AiOther &&
                    command.Content != null &&
                    command.Content.PostType == "posts" &&
                    command.Content.ResourceList != null &&
                    command.Content.ResourceList.Count == 1),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PostResponse(
                runtimePostId,
                userId,
                "user",
                null,
                workspaceId,
                Guid.NewGuid(),
                null,
                null,
                "KQXS Miền Bắc",
                new PostContent
                {
                    Content = "Kết quả xổ số miền Bắc hôm nay: 12345",
                    Hashtag = "#xoso #mienbac",
                    PostType = "posts",
                    ResourceList = []
                },
                "draft",
                null,
                false,
                [],
                [],
                DateTime.UtcNow,
                DateTime.UtcNow)));
        mediator
            .Setup(m => m.Send(
                It.Is<PublishPostsCommand>(command =>
                    command.UserId == userId &&
                    command.Targets.Count == 1 &&
                    command.Targets[0].PostId == runtimePostId &&
                    command.Targets[0].PublishingScheduleId == scheduleId &&
                    command.Targets[0].SocialMediaIds.Count == 1 &&
                    command.Targets[0].SocialMediaIds[0] == socialMediaId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PublishPostsResponse(
            [
                new PublishPostResponse(
                    runtimePostId,
                    "processing",
                    [
                        new PublishPostDestinationResult(
                            socialMediaId,
                            "facebook",
                            string.Empty,
                            string.Empty,
                            Guid.NewGuid(),
                            "processing")
                    ])
            ])));

        var handler = new HandleAgentScheduleRuntimeResultCommandHandler(
            scheduleRepository.Object,
            runtimeContentService.Object,
            webSearchEnrichmentService.Object,
            mediator.Object);

        var result = await handler.Handle(
            new HandleAgentScheduleRuntimeResultCommand(
                scheduleId,
                callbackJobId,
                new N8nWebSearchResponse(
                    "kết quả xổ số miền bắc hôm nay",
                    DateTime.UtcNow,
                    [
                        new N8nWebSearchResultItem(
                            "KQXS",
                            "https://example.com",
                            "Mô tả",
                            "example.com")
                    ],
                    "context")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();

        schedule.Status.Should().Be(PublishingScheduleState.StatusPublishing);
        schedule.Items.Should().ContainSingle(item =>
            item.ItemId == runtimePostId &&
            item.ItemType == PublishingScheduleState.ItemTypePost &&
            item.Status == PublishingScheduleState.ItemStatusPublishing);

        var updatedContext = AgenticScheduleExecutionContextSerializer.Parse(schedule.ExecutionContextJson);
        updatedContext.LastProcessedCallbackJobId.Should().Be(callbackJobId);
        updatedContext.RuntimePostId.Should().Be(runtimePostId);
        updatedContext.LastQuery.Should().Be("kết quả xổ số miền bắc hôm nay");

        scheduleRepository.VerifyAll();
        webSearchEnrichmentService.VerifyAll();
        runtimeContentService.VerifyAll();
        mediator.VerifyAll();
    }
}
