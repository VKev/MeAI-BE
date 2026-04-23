using Application.Posts.Commands;
using Application.Posts.Models;
using Domain.Repositories;
using FluentAssertions;
using Infrastructure.Logic.Services;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace AiMicroservice.Tests.Infrastructure.Logic.Services;

public sealed class ScheduledPostDispatchServiceTests
{
    [Fact]
    public async Task DispatchDuePostsAsync_ShouldSendPublishCommandForClaimedPosts()
    {
        var postId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var socialMediaId = Guid.NewGuid();

        var postRepository = new Mock<IPostRepository>();
        postRepository
            .Setup(repository => repository.ClaimDueScheduledPostsAsync(
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ScheduledPostDispatchCandidate(postId, userId, [socialMediaId], true)
            ]);

        var mediator = new Mock<IMediator>();
        mediator
            .Setup(instance => instance.Send(
                It.Is<PublishPostsCommand>(command =>
                    command.UserId == userId &&
                    command.Targets.Count == 1 &&
                    command.Targets[0].PostId == postId &&
                    command.Targets[0].SocialMediaIds.Count == 1 &&
                    command.Targets[0].SocialMediaIds[0] == socialMediaId &&
                    command.Targets[0].IsPrivate == true),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PublishPostsResponse([])));

        var service = new ScheduledPostDispatchService(
            postRepository.Object,
            mediator.Object,
            Mock.Of<ILogger<ScheduledPostDispatchService>>());

        var dispatchedCount = await service.DispatchDuePostsAsync(CancellationToken.None);

        dispatchedCount.Should().Be(1);
        postRepository.Verify(repository => repository.MarkScheduledDispatchFailedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DispatchDuePostsAsync_ShouldMarkPostFailedWhenPublishCommandFails()
    {
        var postId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var postRepository = new Mock<IPostRepository>();
        postRepository
            .Setup(repository => repository.ClaimDueScheduledPostsAsync(
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ScheduledPostDispatchCandidate(postId, userId, [Guid.NewGuid()], null)
            ]);

        var mediator = new Mock<IMediator>();
        mediator
            .Setup(instance => instance.Send(
                It.IsAny<PublishPostsCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<PublishPostsResponse>(
                new Error("SocialMedia.NotFound", "Social media account not found.")));

        var service = new ScheduledPostDispatchService(
            postRepository.Object,
            mediator.Object,
            Mock.Of<ILogger<ScheduledPostDispatchService>>());

        var dispatchedCount = await service.DispatchDuePostsAsync(CancellationToken.None);

        dispatchedCount.Should().Be(1);
        postRepository.Verify(
            repository => repository.MarkScheduledDispatchFailedAsync(postId, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
