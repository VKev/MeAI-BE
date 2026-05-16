using Application.Abstractions.Agents;
using Application.Agents;
using Application.Agents.Commands;
using Application.Agents.Models;
using Application.ChatSessions;
using Application.PublishingSchedules.Models;
using Domain.Entities;
using Domain.Repositories;
using FluentAssertions;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace AiMicroservice.Tests.Application.Agents.Commands;

public sealed class SendAgentMessageCommandTests
{
    [Fact]
    public async Task Handle_ShouldFail_WhenMessageIsMissing()
    {
        var chatSessionRepository = new Mock<IChatSessionRepository>(MockBehavior.Strict);
        var chatRepository = new Mock<IChatRepository>(MockBehavior.Strict);
        var agentChatService = new Mock<IAgentChatService>(MockBehavior.Strict);

        var handler = new SendAgentMessageCommandHandler(
            chatSessionRepository.Object,
            chatRepository.Object,
            agentChatService.Object);

        var result = await handler.Handle(
            new SendAgentMessageCommand(Guid.NewGuid(), Guid.NewGuid(), "   "),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AgentErrors.InvalidMessage);
        chatSessionRepository.VerifyNoOtherCalls();
        chatRepository.VerifyNoOtherCalls();
        agentChatService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_ShouldFail_WhenSessionBelongsToAnotherUser()
    {
        var session = new ChatSession
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            WorkspaceId = Guid.NewGuid()
        };

        var chatSessionRepository = new Mock<IChatSessionRepository>(MockBehavior.Strict);
        var chatRepository = new Mock<IChatRepository>(MockBehavior.Strict);
        chatSessionRepository
            .Setup(repository => repository.GetByIdForUpdateAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var agentChatService = new Mock<IAgentChatService>(MockBehavior.Strict);

        var handler = new SendAgentMessageCommandHandler(
            chatSessionRepository.Object,
            chatRepository.Object,
            agentChatService.Object);

        var result = await handler.Handle(
            new SendAgentMessageCommand(session.Id, Guid.NewGuid(), "Hello"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ChatSessionErrors.Unauthorized);
        chatSessionRepository.VerifyAll();
        chatRepository.VerifyNoOtherCalls();
        agentChatService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_ShouldReturnTransientMessages_WhenReplySucceeds()
    {
        var userId = Guid.NewGuid();
        var postId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var postBuilderId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var session = new ChatSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            WorkspaceId = Guid.NewGuid()
        };

        var chatSessionRepository = new Mock<IChatSessionRepository>(MockBehavior.Strict);
        var chatRepository = new Mock<IChatRepository>(MockBehavior.Strict);
        chatSessionRepository
            .Setup(repository => repository.GetByIdForUpdateAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        chatRepository
            .Setup(repository => repository.AddAsync(It.IsAny<Chat>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        chatRepository
            .Setup(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        var agentChatService = new Mock<IAgentChatService>(MockBehavior.Strict);
        agentChatService
            .Setup(service => service.GenerateReplyAsync(
                It.Is<AgentChatRequest>(request =>
                    request.UserId == userId &&
                    request.SessionId == session.Id &&
                    request.WorkspaceId == session.WorkspaceId &&
                    request.Message == "Schedule a post for 5pm" &&
                    request.AssistantChatId.HasValue &&
                    request.AssistantChatId.Value != Guid.Empty),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AgentChatCompletionResult(
                "Draft post created for later scheduling.",
                "gemini-3.1-flash-lite-preview",
                ["create_post"],
                [
                    new AgentActionResponse(
                        "post_create",
                        "create_post",
                        "completed",
                        "post",
                        postId,
                        "Draft",
                        "Created draft post.")
                ],
                "post_created",
                null,
                null,
                PostId: postId,
                PostBuilderId: postBuilderId,
                PostIds: [postId])));

        var handler = new SendAgentMessageCommandHandler(
            chatSessionRepository.Object,
            chatRepository.Object,
            agentChatService.Object);

        var result = await handler.Handle(
            new SendAgentMessageCommand(session.Id, userId, "  Schedule a post for 5pm  "),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.SessionId.Should().Be(session.Id);
        result.Value.Action.Should().Be("post_created");
        result.Value.PostId.Should().Be(postId);
        result.Value.PostBuilderId.Should().Be(postBuilderId);
        result.Value.PostIds.Should().BeEquivalentTo([postId]);
        result.Value.ValidationError.Should().BeNull();
        result.Value.UserMessage.Role.Should().Be("user");
        result.Value.UserMessage.Content.Should().Be("Schedule a post for 5pm");
        result.Value.AssistantMessage.Role.Should().Be("assistant");
        result.Value.AssistantMessage.Model.Should().Be("gemini-3.1-flash-lite-preview");
        result.Value.AssistantMessage.ToolNames.Should().BeEquivalentTo(["create_post"]);
        result.Value.AssistantMessage.Actions.Should().ContainSingle(action =>
            action.ToolName == "create_post" &&
            action.Status == "completed");
        chatRepository.Verify(repository => repository.AddAsync(
            It.Is<Chat>(chat => chat.Prompt == "Schedule a post for 5pm"),
            It.IsAny<CancellationToken>()), Times.Once);
        chatRepository.Verify(repository => repository.AddAsync(
            It.Is<Chat>(chat => chat.Prompt == "Draft post created for later scheduling."),
            It.IsAny<CancellationToken>()), Times.Once);

        chatSessionRepository.VerifyAll();
        chatRepository.VerifyAll();
        agentChatService.VerifyAll();
    }

    [Fact]
    public async Task Handle_ShouldReturnValidationFields_WhenReplyRequiresClarification()
    {
        var userId = Guid.NewGuid();
        var session = new ChatSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            WorkspaceId = Guid.NewGuid()
        };

        var chatSessionRepository = new Mock<IChatSessionRepository>(MockBehavior.Strict);
        var chatRepository = new Mock<IChatRepository>(MockBehavior.Strict);
        chatSessionRepository
            .Setup(repository => repository.GetByIdForUpdateAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        chatRepository
            .Setup(repository => repository.AddAsync(It.IsAny<Chat>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        chatRepository
            .Setup(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var agentChatService = new Mock<IAgentChatService>(MockBehavior.Strict);
        agentChatService
            .Setup(service => service.GenerateReplyAsync(
                It.Is<AgentChatRequest>(request =>
                    request.UserId == userId &&
                    request.SessionId == session.Id &&
                    request.WorkspaceId == session.WorkspaceId &&
                    request.Message == "hay tao hinh anh ve doi bong toi yeu" &&
                    request.AssistantChatId.HasValue &&
                    request.AssistantChatId.Value != Guid.Empty),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AgentChatCompletionResult(
                "Yeu cau chua ro doi bong nao.",
                "gemini-3.1-flash-lite-preview",
                [],
                [],
                "validation_failed",
                "Yeu cau chua xac dinh doi bong nao.",
                "hay tao hinh anh ve doi bong {{ten doi bong}}")));

        var handler = new SendAgentMessageCommandHandler(
            chatSessionRepository.Object,
            chatRepository.Object,
            agentChatService.Object);

        var result = await handler.Handle(
            new SendAgentMessageCommand(session.Id, userId, "hay tao hinh anh ve doi bong toi yeu"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Action.Should().Be("validation_failed");
        result.Value.ValidationError.Should().Be("Yeu cau chua xac dinh doi bong nao.");
        result.Value.RevisedPrompt.Should().Be("hay tao hinh anh ve doi bong {{ten doi bong}}");
        result.Value.PostId.Should().BeNull();
        result.Value.ChatId.Should().BeNull();
        chatRepository.Verify(repository => repository.AddAsync(
            It.Is<Chat>(chat => chat.Prompt == "hay tao hinh anh ve doi bong toi yeu"),
            It.IsAny<CancellationToken>()), Times.Once);
        chatRepository.Verify(repository => repository.AddAsync(
            It.Is<Chat>(chat => chat.Prompt == "Yeu cau chua ro doi bong nao."),
            It.IsAny<CancellationToken>()), Times.Never);

        chatSessionRepository.VerifyAll();
        chatRepository.VerifyAll();
        agentChatService.VerifyAll();
    }

    [Fact]
    public async Task Handle_ShouldForwardScheduleOptionsAndReturnScheduleId()
    {
        var userId = Guid.NewGuid();
        var socialMediaId = Guid.NewGuid();
        var scheduleId = Guid.NewGuid();
        var session = new ChatSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            WorkspaceId = Guid.NewGuid()
        };

        var scheduleOptions = new AgentScheduleOptions(
            DateTime.UtcNow.AddHours(3),
            "Asia/Ho_Chi_Minh",
            280,
            [new PublishingScheduleTargetInput(socialMediaId, true)]);

        var chatSessionRepository = new Mock<IChatSessionRepository>(MockBehavior.Strict);
        var chatRepository = new Mock<IChatRepository>(MockBehavior.Strict);
        chatSessionRepository
            .Setup(repository => repository.GetByIdForUpdateAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        chatRepository
            .Setup(repository => repository.AddAsync(It.IsAny<Chat>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        chatRepository
            .Setup(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        var agentChatService = new Mock<IAgentChatService>(MockBehavior.Strict);
        agentChatService
            .Setup(service => service.GenerateReplyAsync(
                It.Is<AgentChatRequest>(request =>
                    request.UserId == userId &&
                    request.SessionId == session.Id &&
                    request.WorkspaceId == session.WorkspaceId &&
                    request.Message == "Dang bai tong hop xu huong AI toi 6h toi nay" &&
                    request.ScheduleOptions != null &&
                    request.ScheduleOptions.Timezone == "Asia/Ho_Chi_Minh" &&
                    request.ScheduleOptions.MaxContentLength == 280 &&
                    request.ScheduleOptions.Targets != null &&
                    request.ScheduleOptions.Targets.Count == 1 &&
                    request.ScheduleOptions.Targets[0].SocialMediaId == socialMediaId &&
                    request.AssistantChatId.HasValue &&
                    request.AssistantChatId.Value != Guid.Empty),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AgentChatCompletionResult(
                "Future AI schedule created.",
                "gemini-3.1-flash-lite-preview",
                ["create_agentic_schedule"],
                [
                    new AgentActionResponse(
                        "schedule_create",
                        "create_agentic_schedule",
                        "completed",
                        "schedule",
                        scheduleId,
                        "AI trend post",
                        "Future AI schedule created.")
                ],
                "future_ai_schedule_created",
                null,
                null,
                null,
                scheduleId)));

        var handler = new SendAgentMessageCommandHandler(
            chatSessionRepository.Object,
            chatRepository.Object,
            agentChatService.Object);

        var result = await handler.Handle(
            new SendAgentMessageCommand(
                session.Id,
                userId,
                "Dang bai tong hop xu huong AI toi 6h toi nay",
                null,
                null,
                scheduleOptions),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Action.Should().Be("future_ai_schedule_created");
        result.Value.ScheduleId.Should().Be(scheduleId);
        result.Value.PostId.Should().BeNull();

        chatSessionRepository.VerifyAll();
        chatRepository.VerifyAll();
        agentChatService.VerifyAll();
    }
}
