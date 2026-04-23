using Application.Abstractions.Agents;
using Application.Agents;
using Application.Agents.Commands;
using Application.ChatSessions;
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
        var userId = Guid.NewGuid();
        var session = new ChatSession
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            WorkspaceId = Guid.NewGuid()
        };

        var chatSessionRepository = new Mock<IChatSessionRepository>(MockBehavior.Strict);
        chatSessionRepository
            .Setup(repository => repository.GetByIdForUpdateAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var chatRepository = new Mock<IChatRepository>(MockBehavior.Strict);
        var agentChatService = new Mock<IAgentChatService>(MockBehavior.Strict);

        var handler = new SendAgentMessageCommandHandler(
            chatSessionRepository.Object,
            chatRepository.Object,
            agentChatService.Object);

        var result = await handler.Handle(
            new SendAgentMessageCommand(session.Id, userId, "Hello"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ChatSessionErrors.Unauthorized);
        chatSessionRepository.VerifyAll();
        chatRepository.VerifyNoOtherCalls();
        agentChatService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_ShouldPersistUserAndAssistantMessages_WhenReplySucceeds()
    {
        var userId = Guid.NewGuid();
        var session = new ChatSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            WorkspaceId = Guid.NewGuid()
        };
        var storedChats = new List<Chat>();

        var chatSessionRepository = new Mock<IChatSessionRepository>(MockBehavior.Strict);
        chatSessionRepository
            .Setup(repository => repository.GetByIdForUpdateAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        chatSessionRepository
            .Setup(repository => repository.Update(session));

        var chatRepository = new Mock<IChatRepository>(MockBehavior.Strict);
        chatRepository
            .Setup(repository => repository.AddAsync(It.IsAny<Chat>(), It.IsAny<CancellationToken>()))
            .Callback<Chat, CancellationToken>((chat, _) => storedChats.Add(chat))
            .Returns(Task.CompletedTask);
        chatRepository
            .Setup(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var agentChatService = new Mock<IAgentChatService>(MockBehavior.Strict);
        agentChatService
            .Setup(service => service.GenerateReplyAsync(
                new AgentChatRequest(userId, session.Id, session.WorkspaceId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AgentChatCompletionResult(
                "Scheduled. I will prepare the slot.",
                "gemini-2.0-flash",
                ["get_user_workspaces", "get_linked_social_accounts"])));

        var handler = new SendAgentMessageCommandHandler(
            chatSessionRepository.Object,
            chatRepository.Object,
            agentChatService.Object);

        var result = await handler.Handle(
            new SendAgentMessageCommand(session.Id, userId, "  Schedule a post for 5pm  "),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        storedChats.Should().HaveCount(2);
        storedChats[0].Prompt.Should().Be("Schedule a post for 5pm");
        storedChats[1].Prompt.Should().Be("Scheduled. I will prepare the slot.");

        var userMetadata = AgentMessageConfigSerializer.Parse(storedChats[0].Config);
        userMetadata.Role.Should().Be("user");

        var assistantMetadata = AgentMessageConfigSerializer.Parse(storedChats[1].Config);
        assistantMetadata.Role.Should().Be("assistant");
        assistantMetadata.Model.Should().Be("gemini-2.0-flash");
        assistantMetadata.ToolNames.Should().BeEquivalentTo(["get_user_workspaces", "get_linked_social_accounts"]);

        result.Value.SessionId.Should().Be(session.Id);
        result.Value.UserMessage.Role.Should().Be("user");
        result.Value.UserMessage.Content.Should().Be("Schedule a post for 5pm");
        result.Value.AssistantMessage.Role.Should().Be("assistant");
        result.Value.AssistantMessage.Model.Should().Be("gemini-2.0-flash");
        result.Value.AssistantMessage.ToolNames.Should().BeEquivalentTo(["get_user_workspaces", "get_linked_social_accounts"]);

        session.UpdatedAt.Should().NotBeNull();
        chatSessionRepository.Verify(repository => repository.Update(session), Times.Exactly(2));
        chatRepository.Verify(repository => repository.AddAsync(It.IsAny<Chat>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        chatRepository.Verify(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        agentChatService.VerifyAll();
        chatSessionRepository.VerifyAll();
        chatRepository.VerifyAll();
    }
}
