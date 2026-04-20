using Application.Abstractions.Ai;
using Application.Abstractions.Data;
using Application.Abstractions.Notifications;
using Application.Abstractions.Resources;
using Application.Posts.Commands;
using Application.Posts.Models;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Context;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace test;

public sealed class CreatePostCommandTests
{
    [Fact]
    public async Task Handle_Should_CreatePostWithNewHashtag_WithoutConcurrencyException()
    {
        await using var dbContext = CreateDbContext();
        using var unitOfWork = new UnitOfWork(dbContext);

        var userId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();

        var userResourceService = new Mock<IUserResourceService>();
        userResourceService
            .Setup(service => service.GetPresignedResourcesAsync(
                userId,
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(resourceId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<UserResourcePresignResult>>(
                new[]
                {
                    new UserResourcePresignResult(resourceId, "https://cdn.example.com/media.gif", "image/gif", "gif")
                }));

        var aiFeedPostService = new Mock<IAiFeedPostService>();
        aiFeedPostService
            .Setup(service => service.CreateMirrorPostAsync(It.IsAny<CreateAiMirrorPostRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AiFeedMirrorPostResult(Guid.NewGuid(), DateTime.UtcNow)));

        var feedNotificationService = new Mock<IFeedNotificationService>();
        feedNotificationService
            .Setup(service => service.NotifyNewPostAsync(
                userId,
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<Guid>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new CreatePostCommandHandler(
            unitOfWork,
            userResourceService.Object,
            aiFeedPostService.Object,
            feedNotificationService.Object);

        var command = new CreatePostCommand(
            userId,
            "My another test post #8386",
            new[] { resourceId },
            "gif");

        var result = await handler.Handle(command, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();

        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var savedPost = await dbContext.Posts.SingleAsync();
        savedPost.Content.Should().Be("My another test post #8386");
        savedPost.MediaType.Should().Be("gif");
        savedPost.ResourceIds.Should().ContainSingle().Which.Should().Be(resourceId);

        var savedHashtag = await dbContext.Hashtags.SingleAsync();
        savedHashtag.Name.Should().Be("#8386");
        savedHashtag.PostCount.Should().Be(1);

        var savedPostHashtag = await dbContext.PostHashtags.SingleAsync();
        savedPostHashtag.PostId.Should().Be(savedPost.Id);
        savedPostHashtag.HashtagId.Should().Be(savedHashtag.Id);
    }

    private static MyDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new MyDbContext(options);
    }
}
