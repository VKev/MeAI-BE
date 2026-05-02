using Application.Agents;
using Application.Agents.Queries;
using Application.Agents.Models;
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
        var chatRepository = new Mock<IChatRepository>(MockBehavior.Strict);
        chatSessionRepository
            .Setup(repository => repository.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatSession?)null);

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
        var chatRepository = new Mock<IChatRepository>(MockBehavior.Strict);
        chatSessionRepository
            .Setup(repository => repository.GetByIdAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

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
    public async Task Handle_ShouldReturnPersistedMessages_WhenSessionIsOwned()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var chatSessionRepository = new Mock<IChatSessionRepository>(MockBehavior.Strict);
        var chatRepository = new Mock<IChatRepository>(MockBehavior.Strict);
        chatSessionRepository
            .Setup(repository => repository.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatSession
            {
                Id = sessionId,
                UserId = userId,
                WorkspaceId = Guid.NewGuid()
            });
        chatRepository
            .Setup(repository => repository.GetBySessionIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new Chat
                {
                    Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    SessionId = sessionId,
                    Prompt = "hello",
                    Config = AgentMessageConfigSerializer.Serialize(new AgentChatMetadata(Role: "user")),
                    CreatedAt = new DateTime(2026, 4, 30, 10, 0, 0, DateTimeKind.Utc)
                },
                new Chat
                {
                    Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                    SessionId = sessionId,
                    Prompt = "draft created",
                    Config = AgentMessageConfigSerializer.Serialize(new AgentChatMetadata(
                        Role: "assistant",
                        Model: "gemini-test",
                        ToolNames: ["create_post"],
                        Actions: [new AgentActionResponse("post_create", "create_post", "completed")])),
                    CreatedAt = new DateTime(2026, 4, 30, 10, 0, 1, DateTimeKind.Utc)
                }
            ]);

        var handler = new GetAgentSessionMessagesQueryHandler(chatSessionRepository.Object, chatRepository.Object);

        var result = await handler.Handle(
            new GetAgentSessionMessagesQuery(sessionId, userId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value[0].Role.Should().Be("user");
        result.Value[0].Content.Should().Be("hello");
        result.Value[1].Role.Should().Be("assistant");
        result.Value[1].Model.Should().Be("gemini-test");
        result.Value[1].ToolNames.Should().BeEquivalentTo(["create_post"]);
        chatSessionRepository.VerifyAll();
        chatRepository.VerifyAll();
    }
}
