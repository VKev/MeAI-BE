using Application.Abstractions.Agents;
using Application.Abstractions.ApiCredentials;
using Application.Abstractions.Configs;
using Application.Abstractions.SocialMedias;
using Application.PublishingSchedules.Models;
using Application.Recommendations.Services;
using FluentAssertions;
using Infrastructure.Logic.Agents;
using Infrastructure.Logic.Kie;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;
using System.Text;
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
        credentialProvider
            .Setup(provider => provider.GetRequiredValue("Kie", "ApiKey"))
            .Returns("unit-test-key");
        var httpClientFactory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "output": [
                    {
                      "type": "message",
                      "role": "assistant",
                      "tool_calls": [
                        {
                          "id": "call_123",
                          "type": "function",
                          "function": {
                            "name": "analyze_schedule_request",
                            "arguments": "{\"action\":\"post_created\",\"assistantMessage\":\"future schedule\",\"validationError\":null,\"revisedPrompt\":\"Đăng bài về đội tuyển vô địch World Cup năm nay dựa trên kết quả thực tế tại thời điểm chạy.\",\"finalPrompt\":\"Đăng bài về đội tuyển vô địch World Cup năm nay dựa trên kết quả thực tế tại thời điểm chạy.\",\"title\":\"Bài đăng đội tuyển vô địch World Cup\",\"postType\":\"posts\"}"
                          }
                        }
                      ]
                    }
                  ]
                }
                """,
                Encoding.UTF8,
                "application/json")
        });
        httpClientFactory
            .Setup(factory => factory.CreateClient("KieChat"))
            .Returns(new HttpClient(handler));
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
                    request.UserPrompt.Contains("thời điểm chạy", StringComparison.OrdinalIgnoreCase) &&
                    request.Platform == "facebook"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new QueryRewriteResult(
                "vi",
                "informational",
                "đội tuyển vô địch World Cup năm nay",
                [],
                "football team celebrating championship",
                ["World Cup"])));

        var userSocialMediaService = new Mock<IUserSocialMediaService>(MockBehavior.Strict);
        userSocialMediaService
            .Setup(service => service.GetSocialMediasAsync(
                userId,
                It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == socialMediaId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<UserSocialMediaResult>>(
                [new UserSocialMediaResult(socialMediaId, "facebook", null)]));

        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        mediator
            .Setup(service => service.Send(
                It.Is<global::Application.PublishingSchedules.Commands.CreateAgenticPublishingScheduleCommand>(command =>
                    command.UserId == userId &&
                    command.WorkspaceId == workspaceId &&
                    command.MaxContentLength == 280 &&
                    command.AgentPrompt != null &&
                    command.AgentPrompt.Contains("vô địch", StringComparison.OrdinalIgnoreCase) &&
                    command.DesiredPostType == "posts" &&
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
        var kieResponsesClient = new KieResponsesClient(
            configuration,
            httpClientFactory.Object,
            credentialProvider.Object,
            Mock.Of<ILogger<KieResponsesClient>>());

        var service = new GeminiAgentChatService(
            configuration,
            kieResponsesClient,
            userConfigService.Object,
            userSocialMediaService.Object,
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
        userSocialMediaService.VerifyAll();
        mediator.VerifyAll();
        chatWebPostService.VerifyNoOtherCalls();
        httpClientFactory.VerifyAll();
        credentialProvider.VerifyAll();
    }

    [Fact]
    public async Task GenerateReplyAsync_ShouldForceReels_ForTikTokAgenticSchedule()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var socialMediaId = Guid.NewGuid();
        var scheduleId = Guid.NewGuid();

        var configuration = new ConfigurationBuilder().Build();

        var credentialProvider = new Mock<IApiCredentialProvider>(MockBehavior.Strict);
        credentialProvider
            .Setup(provider => provider.GetRequiredValue("Kie", "ApiKey"))
            .Returns("unit-test-key");
        var httpClientFactory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "output": [
                    {
                      "type": "message",
                      "role": "assistant",
                      "tool_calls": [
                        {
                          "id": "call_123",
                          "type": "function",
                          "function": {
                            "name": "analyze_schedule_request",
                            "arguments": "{\"action\":\"post_created\",\"assistantMessage\":\"future schedule\",\"validationError\":null,\"revisedPrompt\":null,\"finalPrompt\":\"Tóm tắt tin AI nóng nhất tại thời điểm chạy để đăng TikTok.\",\"title\":\"TikTok AI update\",\"postType\":\"posts\"}"
                          }
                        }
                      ]
                    }
                  ]
                }
                """,
                Encoding.UTF8,
                "application/json")
        });
        httpClientFactory
            .Setup(factory => factory.CreateClient("KieChat"))
            .Returns(new HttpClient(handler));

        var userConfigService = new Mock<IUserConfigService>(MockBehavior.Strict);
        userConfigService
            .Setup(service => service.GetActiveConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<UserAiConfig?>(new UserAiConfig(
                Guid.NewGuid(),
                "gemini-3.1-flash-lite-preview",
                null,
                null)));

        var userSocialMediaService = new Mock<IUserSocialMediaService>(MockBehavior.Strict);
        userSocialMediaService
            .Setup(service => service.GetSocialMediasAsync(
                userId,
                It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == socialMediaId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<UserSocialMediaResult>>(
                [new UserSocialMediaResult(socialMediaId, "tiktok", null)]));

        var queryRewriter = new Mock<IQueryRewriter>(MockBehavior.Strict);
        queryRewriter
            .Setup(service => service.RewriteAsync(
                It.Is<QueryRewriteRequest>(request =>
                    request.Platform == "tiktok" &&
                    request.UserPrompt.Contains("TikTok", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new QueryRewriteResult(
                "vi",
                "informational",
                "tin AI nóng nhất cho TikTok",
                [],
                "ai breaking news vertical video",
                ["AI"])));

        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        mediator
            .Setup(service => service.Send(
                It.Is<global::Application.PublishingSchedules.Commands.CreateAgenticPublishingScheduleCommand>(command =>
                    command.UserId == userId &&
                    command.WorkspaceId == workspaceId &&
                    command.DesiredPostType == "reels" &&
                    command.PlatformPreference == "tiktok"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PublishingScheduleResponse(
                scheduleId,
                userId,
                workspaceId,
                "TikTok AI update",
                "agentic",
                "waiting_for_execution",
                DateTime.UtcNow.AddHours(5),
                "Asia/Ho_Chi_Minh",
                false,
                "user",
                "tiktok",
                "Tóm tắt tin AI nóng nhất tại thời điểm chạy để đăng TikTok.",
                220,
                new PublishingScheduleSearchInput("tin AI nóng nhất cho TikTok", 5, null, "vi", "pd"),
                null,
                [],
                [new PublishingScheduleTargetResponse(Guid.NewGuid(), socialMediaId, "tiktok", "tiktok", true)],
                null,
                null,
                null,
                null,
                DateTime.UtcNow,
                DateTime.UtcNow)));

        var chatWebPostService = new Mock<IChatWebPostService>(MockBehavior.Strict);
        var kieResponsesClient = new KieResponsesClient(
            configuration,
            httpClientFactory.Object,
            credentialProvider.Object,
            Mock.Of<ILogger<KieResponsesClient>>());

        var service = new GeminiAgentChatService(
            configuration,
            kieResponsesClient,
            userConfigService.Object,
            userSocialMediaService.Object,
            mediator.Object,
            chatWebPostService.Object,
            queryRewriter.Object,
            Mock.Of<ILogger<GeminiAgentChatService>>());

        var result = await service.GenerateReplyAsync(
            new AgentChatRequest(
                userId,
                sessionId,
                workspaceId,
                "Hãy theo dõi tin AI nóng nhất và đăng TikTok cho tôi",
                null,
                null,
                new AgentScheduleOptions(
                    DateTime.UtcNow.AddHours(5),
                    "Asia/Ho_Chi_Minh",
                    220,
                    [new PublishingScheduleTargetInput(socialMediaId, true)])),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Action.Should().Be("future_ai_schedule_created");
        result.Value.ScheduleId.Should().Be(scheduleId);

        userConfigService.VerifyAll();
        userSocialMediaService.VerifyAll();
        queryRewriter.VerifyAll();
        mediator.VerifyAll();
        chatWebPostService.VerifyNoOtherCalls();
        httpClientFactory.VerifyAll();
        credentialProvider.VerifyAll();
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public StubHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_response);
        }
    }
}
