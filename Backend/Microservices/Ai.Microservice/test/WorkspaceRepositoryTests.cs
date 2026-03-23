using Application.Abstractions.Workspaces;
using FluentAssertions;
using Infrastructure.Repositories;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace test;

public sealed class WorkspaceRepositoryTests
{
    [Fact]
    public async Task ExistsForUserAsync_ShouldReturnTrue_WhenRemoteWorkspaceExists()
    {
        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var userWorkspaceService = new Mock<IUserWorkspaceService>();
        userWorkspaceService
            .Setup(service => service.GetWorkspaceAsync(userId, workspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<UserWorkspaceResult?>(new UserWorkspaceResult(
                workspaceId,
                "MyPost",
                null,
                null,
                new DateTime(2026, 03, 23, 21, 32, 00, DateTimeKind.Utc),
                null)));

        var repository = new WorkspaceRepository(userWorkspaceService.Object);

        var exists = await repository.ExistsForUserAsync(workspaceId, userId, CancellationToken.None);

        exists.Should().BeTrue();
        userWorkspaceService.VerifyAll();
    }

    [Fact]
    public async Task ExistsForUserAsync_ShouldReturnFalse_WhenRemoteWorkspaceDoesNotExist()
    {
        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var userWorkspaceService = new Mock<IUserWorkspaceService>();
        userWorkspaceService
            .Setup(service => service.GetWorkspaceAsync(userId, workspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<UserWorkspaceResult?>(null));

        var repository = new WorkspaceRepository(userWorkspaceService.Object);

        var exists = await repository.ExistsForUserAsync(workspaceId, userId, CancellationToken.None);

        exists.Should().BeFalse();
        userWorkspaceService.VerifyAll();
    }
}
