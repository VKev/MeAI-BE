using Application.ChatSessions;
using Application.ChatSessions.Commands;
using Domain.Repositories;
using FluentAssertions;
using Moq;

namespace test;

public sealed class CreateChatSessionCommandTests
{
    [Fact]
    public async Task Handle_ShouldReturnWorkspaceNotFound_WhenWorkspaceDoesNotExist()
    {
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();

        var chatSessionRepository = new Mock<IChatSessionRepository>(MockBehavior.Strict);
        var workspaceRepository = new Mock<IWorkspaceRepository>();
        workspaceRepository
            .Setup(repository => repository.ExistsForUserAsync(workspaceId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = new CreateChatSessionCommandHandler(chatSessionRepository.Object, workspaceRepository.Object);

        var result = await handler.Handle(
            new CreateChatSessionCommand(userId, workspaceId, "Session"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ChatSessionErrors.WorkspaceNotFound);
        chatSessionRepository.VerifyNoOtherCalls();
        workspaceRepository.VerifyAll();
    }
}
