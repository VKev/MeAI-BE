using Application.Agents;
using Application.Agents.Models;
using Application.Agents.Queries;
using Application.ChatSessions;
using Domain.Entities;
using Domain.Repositories;
using FluentAssertions;
using Moq;

namespace AiMicroservice.Tests.Application.Agents.Queries;

public sealed class GetAgentSessionMessagesQueryTests
{
    [Fact]
    public async Task Handle_ShouldReturnNotFound_WhenSessionDoesNotExist()
    {
        var sessionId = Guid.NewGuid();

        var chatSessionRepository = new Mock<IChatSessionRepository>(MockBehavior.Strict);
        chatSessionRepository
            .Setup(repository => repository.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatSession?)null);

        var chatRepository = new Mock<IChatRepository>(MockBehavior.Strict);
        var handler = new GetAgentSessionMessagesQueryHandler(chatSessionRepository.Object, chatRepository.Object);

        var result = await handler.Handle(
            new GetAgentSessionMessagesQuery(sessionId, Guid.NewGuid()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ChatSessionErrors.NotFound);
        chatSessionRepository.VerifyAll();
        chatRepository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_ShouldReturnUnauthorized_WhenSessionBelongsToAnotherUser()
    {
        var session = new ChatSession
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            WorkspaceId = Guid.NewGuid()
        };

        var chatSessionRepository = new Mock<IChatSessionRepository>(MockBehavior.Strict);
        chatSessionRepository
            .Setup(repository => repository.GetByIdAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var chatRepository = new Mock<IChatRepository>(MockBehavior.Strict);
        var handler = new GetAgentSessionMessagesQueryHandler(chatSessionRepository.Object, chatRepository.Object);

        var result = await handler.Handle(
            new GetAgentSessionMessagesQuery(session.Id, Guid.NewGuid()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ChatSessionErrors.Unauthorized);
        chatSessionRepository.VerifyAll();
        chatRepository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_ShouldReturnOrderedMessagesAndParseMetadata()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var createdAt = new DateTime(2026, 04, 23, 10, 00, 00, DateTimeKind.Utc);

        var chatSessionRepository = new Mock<IChatSessionRepository>(MockBehavior.Strict);
        chatSessionRepository
            .Setup(repository => repository.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatSession
            {
                Id = sessionId,
                UserId = userId,
                WorkspaceId = Guid.NewGuid()
            });

        var earliestId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var laterId = Guid.Parse("00000000-0000-0000-0000-0000000000ff");
        var latestId = Guid.Parse("00000000-0000-0000-0000-000000000100");

        var chats = new[]
        {
            new Chat
            {
                Id = latestId,
                SessionId = sessionId,
                Prompt = "Assistant reply",
                Config = AgentMessageConfigSerializer.Serialize(new AgentChatMetadata(
                    Role: "assistant",
                    Model: "gemini-2.0-flash",
                    ToolNames: ["get_posts"])),
                CreatedAt = createdAt.AddMinutes(1)
            },
            new Chat
            {
                Id = laterId,
                SessionId = sessionId,
                Prompt = "Invalid config fallback",
                Config = "{not-json",
                CreatedAt = createdAt
            },
            new Chat
            {
                Id = earliestId,
                SessionId = sessionId,
                Prompt = "User request",
                Config = null,
                CreatedAt = createdAt
            },
            new Chat
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000200"),
                SessionId = sessionId,
                Prompt = "Soft deleted",
                Config = AgentMessageConfigSerializer.Serialize(new AgentChatMetadata(Role: "assistant")),
                CreatedAt = createdAt.AddMinutes(2),
                DeletedAt = createdAt.AddMinutes(3)
            }
        };

        var chatRepository = new Mock<IChatRepository>(MockBehavior.Strict);
        chatRepository
            .Setup(repository => repository.GetBySessionIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(chats);

        var handler = new GetAgentSessionMessagesQueryHandler(chatSessionRepository.Object, chatRepository.Object);

        var result = await handler.Handle(
            new GetAgentSessionMessagesQuery(sessionId, userId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);

        var messages = result.Value.ToArray();
        messages.Select(message => message.Id).Should().ContainInOrder(earliestId, laterId, latestId);

        messages[0].Should().BeEquivalentTo(new AgentMessageResponse(
            earliestId,
            sessionId,
            "user",
            "User request",
            null,
            null,
            null,
            [],
            createdAt,
            null));

        messages[1].Role.Should().Be("user");
        messages[1].ToolNames.Should().BeEmpty();

        messages[2].Role.Should().Be("assistant");
        messages[2].Model.Should().Be("gemini-2.0-flash");
        messages[2].ToolNames.Should().BeEquivalentTo(["get_posts"]);

        chatSessionRepository.VerifyAll();
        chatRepository.VerifyAll();
    }
}
