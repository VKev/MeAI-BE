using Application.Abstractions.Automation;
using Application.Abstractions.Rag;
using Application.Posts.Commands;
using Application.Posts.Models;
using Application.PublishingSchedules;
using Application.PublishingSchedules.Commands;
using Application.PublishingSchedules.Models;
using Application.Recommendations.Commands;
using Application.Recommendations.Models;
using Application.Recommendations.Queries;
using Domain.Entities;
using Domain.Repositories;
using FluentAssertions;
using MediatR;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace AiMicroservice.Tests.Application.PublishingSchedules.Commands;

public sealed class ExecuteAgenticPublishingScheduleCommandTests
{
    [Fact]
    public async Task Handle_ShouldCreateRuntimePostAndPublishIt()
    {
        var scheduleId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var primarySocialMediaId = Guid.NewGuid();
        var socialMediaId = Guid.NewGuid();
        var runtimePostId = Guid.NewGuid();

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
            MaxContentLength = 280,
            ExecutionContextJson = AgenticScheduleExecutionContextSerializer.Serialize(
                new AgenticScheduleExecutionContext(
                    Search: new PublishingScheduleSearchInput(
                        "kết quả xổ số miền bắc hôm nay",
                        5,
                        "VN",
                        "vi",
                        "pd"))),
            Targets =
            [
                new PublishingScheduleTarget
                {
                    Id = Guid.NewGuid(),
                    SocialMediaId = primarySocialMediaId,
                    Platform = "tiktok",
                    TargetLabel = "tiktok",
                    IsPrimary = true
                },
                new PublishingScheduleTarget
                {
                    Id = Guid.NewGuid(),
                    SocialMediaId = socialMediaId,
                    Platform = "facebook",
                    TargetLabel = "facebook",
                    IsPrimary = false
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
        var agentWebSearchService = new Mock<IAgentWebSearchService>(MockBehavior.Strict);
        agentWebSearchService
            .Setup(service => service.SearchAsync(
                It.Is<AgentWebSearchRequest>(search =>
                    search.Query == "kết quả xổ số miền bắc hôm nay" &&
                    search.Count == 5 &&
                    search.UserId == userId &&
                    search.WorkspaceId == workspaceId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AgentWebSearchResponse(
                "kết quả xổ số miền bắc hôm nay",
                DateTime.UtcNow,
                [
                    new AgentWebSearchResultItem(
                        "KQXS",
                        "https://example.com",
                        "Mô tả",
                        "search",
                        "KQXS Mien Bac",
                        "Chi tiet noi dung trang",
                        ["https://example.com/image.jpg"])
                ],
                "context",
                [
                    new ImportedResourceItem(
                        Guid.NewGuid(),
                        "https://cdn.example.com/image.jpg",
                        "image/jpeg",
                        "image",
                        "https://example.com/image.jpg",
                        "https://example.com")
                ])));
        runtimeContentService
            .Setup(service => service.GeneratePostDraftAsync(
                It.Is<AgenticRuntimeContentRequest>(request =>
                    request.ScheduleId == scheduleId &&
                    request.PlatformPreference == "facebook" &&
                    request.MaxContentLength == 280 &&
                    request.GroundingSocialMediaId == socialMediaId &&
                    request.GroundingPlatform == "facebook" &&
                    request.Search.Query == "kết quả xổ số miền bắc hôm nay" &&
                    request.RecommendationSummary == "Đăng kết quả xổ số theo giọng điệu ngắn gọn, rõ số và CTA theo dõi page."),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AgenticRuntimePostDraft(
                "KQXS Miền Bắc",
                "Kết quả xổ số miền Bắc hôm nay: 12345",
                "#xoso #mienbac",
                "posts")));

        var ragClient = new Mock<IRagClient>(MockBehavior.Strict);
        ragClient
            .Setup(client => client.WaitForRagReadyAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        mediator
            .Setup(m => m.Send(
                It.Is<IndexSocialAccountPostsCommand>(command =>
                    command.UserId == userId &&
                    command.SocialMediaId == socialMediaId &&
                    command.MaxPosts == 30),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new IndexSocialAccountPostsResponse(
                socialMediaId,
                "facebook",
                $"facebook:{socialMediaId:N}:",
                10,
                1,
                0,
                9,
                1,
                1,
                0,
                1)));
        mediator
            .Setup(m => m.Send(
                It.Is<QueryAccountRecommendationsQuery>(command =>
                    command.UserId == userId &&
                    command.SocialMediaId == socialMediaId &&
                    command.Query.Contains("Đăng kết quả xổ số miền bắc lên Facebook.", StringComparison.Ordinal) &&
                    command.Query.Contains("kết quả xổ số miền bắc hôm nay", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AccountRecommendationsAnswer(
                "Đăng kết quả xổ số theo giọng điệu ngắn gọn, rõ số và CTA theo dõi page.",
                $"facebook:{socialMediaId:N}:",
                Array.Empty<RecommendationReference>(),
                null,
                "Page profile")));
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
                    command.Targets[0].SocialMediaIds.Count == 2 &&
                    command.Targets[0].SocialMediaIds.Contains(primarySocialMediaId) &&
                    command.Targets[0].SocialMediaIds.Contains(socialMediaId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PublishPostsResponse(
            [
                new PublishPostResponse(
                    runtimePostId,
                    "processing",
                    [
                        new PublishPostDestinationResult(
                            primarySocialMediaId,
                            "tiktok",
                            string.Empty,
                            string.Empty,
                            Guid.NewGuid(),
                            "processing"),
                        new PublishPostDestinationResult(
                            socialMediaId,
                            "facebook",
                            string.Empty,
                            string.Empty,
                            Guid.NewGuid(),
                            "processing")
                    ])
            ])));

        var handler = new ExecuteAgenticPublishingScheduleCommandHandler(
            scheduleRepository.Object,
            runtimeContentService.Object,
            agentWebSearchService.Object,
            mediator.Object,
            ragClient.Object);

        var result = await handler.Handle(
            new ExecuteAgenticPublishingScheduleCommand(scheduleId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();

        schedule.Status.Should().Be(PublishingScheduleState.StatusPublishing);
        schedule.Items.Should().ContainSingle(item =>
            item.ItemId == runtimePostId &&
            item.ItemType == PublishingScheduleState.ItemTypePost &&
            item.Status == PublishingScheduleState.ItemStatusPublishing);

        var updatedContext = AgenticScheduleExecutionContextSerializer.Parse(schedule.ExecutionContextJson);
        updatedContext.LastExecutionRunId.Should().NotBeNull();
        updatedContext.RuntimePostId.Should().Be(runtimePostId);
        updatedContext.LastQuery.Should().Be("kết quả xổ số miền bắc hôm nay");
        updatedContext.GroundingSocialMediaId.Should().Be(socialMediaId);
        updatedContext.LastRecommendationQuery.Should().NotBeNull();
        updatedContext.LastRecommendationSummary.Should().Contain("xổ số");

        scheduleRepository.VerifyAll();
        agentWebSearchService.VerifyAll();
        runtimeContentService.VerifyAll();
        ragClient.VerifyAll();
        mediator.VerifyAll();
    }

    [Fact]
    public async Task Handle_ShouldFallbackToLegacyRuntimeDraft_WhenRagRecommendationFails()
    {
        var scheduleId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var socialMediaId = Guid.NewGuid();
        var runtimePostId = Guid.NewGuid();

        var schedule = new PublishingSchedule
        {
            Id = scheduleId,
            UserId = userId,
            WorkspaceId = workspaceId,
            Name = "Runtime fallback",
            Mode = PublishingScheduleState.AgenticMode,
            Status = PublishingScheduleState.StatusWaitingForExecution,
            Timezone = "UTC",
            ExecuteAtUtc = DateTime.UtcNow.AddHours(1),
            PlatformPreference = "facebook",
            AgentPrompt = "Đăng tin nóng lên Facebook.",
            MaxContentLength = 220,
            ExecutionContextJson = AgenticScheduleExecutionContextSerializer.Serialize(
                new AgenticScheduleExecutionContext(
                    Search: new PublishingScheduleSearchInput("tin nóng AI", 5, null, null, "pd"))),
            Targets =
            [
                new PublishingScheduleTarget
                {
                    Id = Guid.NewGuid(),
                    SocialMediaId = socialMediaId,
                    Platform = "facebook",
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
        runtimeContentService
            .Setup(service => service.GeneratePostDraftAsync(
                It.Is<AgenticRuntimeContentRequest>(request =>
                    request.MaxContentLength == 220 &&
                    request.GroundingSocialMediaId == socialMediaId &&
                    request.RecommendationSummary == null &&
                    !string.IsNullOrWhiteSpace(request.RagFallbackReason)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AgenticRuntimePostDraft(
                "Fallback post",
                "Fallback content from search results",
                null,
                "posts")));

        var agentWebSearchService = new Mock<IAgentWebSearchService>(MockBehavior.Strict);
        agentWebSearchService
            .Setup(service => service.SearchAsync(
                It.IsAny<AgentWebSearchRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AgentWebSearchResponse(
                "tin nóng AI",
                DateTime.UtcNow,
                [
                    new AgentWebSearchResultItem("Tin nóng", "https://example.com", "Mô tả", "search")
                ],
                "context")));

        var ragClient = new Mock<IRagClient>(MockBehavior.Strict);
        ragClient
            .Setup(client => client.WaitForRagReadyAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        mediator
            .Setup(m => m.Send(
                It.IsAny<IndexSocialAccountPostsCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<IndexSocialAccountPostsResponse>(
                new Error("Rag.IndexFailed", "Simulated RAG outage")));
        mediator
            .Setup(m => m.Send(
                It.IsAny<CreatePostCommand>(),
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
                "Fallback post",
                new PostContent
                {
                    Content = "Fallback content from search results",
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
                It.IsAny<PublishPostsCommand>(),
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

        var handler = new ExecuteAgenticPublishingScheduleCommandHandler(
            scheduleRepository.Object,
            runtimeContentService.Object,
            agentWebSearchService.Object,
            mediator.Object,
            ragClient.Object);

        var result = await handler.Handle(
            new ExecuteAgenticPublishingScheduleCommand(scheduleId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        schedule.Status.Should().Be(PublishingScheduleState.StatusPublishing);

        var updatedContext = AgenticScheduleExecutionContextSerializer.Parse(schedule.ExecutionContextJson);
        updatedContext.LastRagFallbackReason.Should().NotBeNullOrWhiteSpace();

        scheduleRepository.VerifyAll();
        runtimeContentService.VerifyAll();
        agentWebSearchService.VerifyAll();
        ragClient.VerifyAll();
        mediator.VerifyAll();
    }
}
