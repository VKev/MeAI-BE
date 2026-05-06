using Application.Abstractions.Agents;
using Application.Abstractions.ApiCredentials;
using Application.Abstractions.Configs;
using Application.PublishingSchedules.Models;
using Application.Recommendations.Services;
using FluentAssertions;
using Infrastructure.Logic.Agents;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace AiMicroservice.Tests.Infrastructure.Logic.Agents;

public sealed class GeminiAgentChatServiceTests
{
    [Fact]
    public async Task GenerateReplyAsync_ShouldInferFutureWinnerPrompt_ForAgenticSchedule()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var socialMediaId = Guid.NewGuid();
        var scheduleId = Guid.NewGuid();

        var configuration = new ConfigurationBuilder().Build();

        var credentialProvider = new Mock<IApiCredentialProvider>(MockBehavior.Strict);
        var userConfigService = new Mock<IUserConfigService>(MockBehavior.Strict);
        userConfigService
            .Setup(service => service.GetActiveConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<UserAiConfig?>(new UserAiConfig(
                Guid.NewGuid(),
                "gemini-3.1-flash-lite-preview",
                null,
                null)));

        var queryRewriter = new Mock<IQueryRewriter>(MockBehavior.Strict);
        queryRewriter
            .Setup(service => service.RewriteAsync(
                It.Is<QueryRewriteRequest>(request =>
                    request.UserPrompt.Contains("vô địch", StringComparison.OrdinalIgnoreCase) &&
                    request.UserPrompt.Contains("thời điểm chạy", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new QueryRewriteResult(
                "vi",
                "informational",
                "đội tuyển vô địch World Cup năm nay",
                [],
                "football team celebrating championship",
                ["World Cup"])));

        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        mediator
            .Setup(service => service.Send(
                It.Is<Application.PublishingSchedules.Commands.CreateAgenticPublishingScheduleCommand>(command =>
                    command.UserId == userId &&
                    command.WorkspaceId == workspaceId &&
                    command.MaxContentLength == 280 &&
                    command.AgentPrompt != null &&
                    command.AgentPrompt.Contains("vô địch", StringComparison.OrdinalIgnoreCase) &&
                    command.Search != null &&
                    command.Search.QueryTemplate == "đội tuyển vô địch World Cup năm nay"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PublishingScheduleResponse(
                scheduleId,
                userId,
                workspaceId,
                "Bài đăng đội tuyển vô địch World Cup",
                "agentic",
                "waiting_for_execution",
                DateTime.UtcNow.AddHours(5),
                "Asia/Ho_Chi_Minh",
                false,
                "user",
                "facebook",
                "Đăng bài về đội tuyển vô địch World Cup năm nay dựa trên kết quả thực tế tại thời điểm chạy.",
                280,
                new PublishingScheduleSearchInput("đội tuyển vô địch World Cup năm nay", 5, null, "vi", "pd"),
                null,
                [],
                [new PublishingScheduleTargetResponse(Guid.NewGuid(), socialMediaId, "facebook", "facebook", true)],
                null,
                null,
                null,
                null,
                DateTime.UtcNow,
                DateTime.UtcNow)));

        var chatWebPostService = new Mock<IChatWebPostService>(MockBehavior.Strict);

        var service = new GeminiAgentChatService(
            configuration,
            credentialProvider.Object,
            userConfigService.Object,
            mediator.Object,
            chatWebPostService.Object,
            queryRewriter.Object,
            Mock.Of<ILogger<GeminiAgentChatService>>());

        var result = await service.GenerateReplyAsync(
            new AgentChatRequest(
                userId,
                sessionId,
                workspaceId,
                "Hãy đăng bài viết về đội tuyển chiến thắng World Cup năm nay",
                null,
                new AgentScheduleOptions(
                    DateTime.UtcNow.AddHours(5),
                    "Asia/Ho_Chi_Minh",
                    280,
                    [new PublishingScheduleTargetInput(socialMediaId, true)])),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Action.Should().Be("future_ai_schedule_created");
        result.Value.ScheduleId.Should().Be(scheduleId);
        result.Value.ValidationError.Should().BeNull();
        result.Value.RevisedPrompt.Should().Contain("vô địch");

        userConfigService.VerifyAll();
        queryRewriter.VerifyAll();
        mediator.VerifyAll();
        chatWebPostService.VerifyNoOtherCalls();
        credentialProvider.VerifyNoOtherCalls();
    }
}
