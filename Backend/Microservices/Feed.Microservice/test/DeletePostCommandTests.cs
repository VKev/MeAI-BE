using Application.Abstractions.Ai;
using Application.Abstractions.Notifications;
using Application.Abstractions.Resources;
using Application.Posts.Commands;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Context;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace test;

public sealed class DeletePostCommandTests
{
    [Fact]
    public async Task Handle_Should_DeleteAttachedResources_WhenDeletingOwnedPost()
    {
        await using var dbContext = CreateDbContext();
        using var unitOfWork = new UnitOfWork(dbContext);

        var userId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var post = new Post
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Content = "Feed post",
            ResourceIds = new[] { resourceId },
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            UpdatedAt = DateTime.UtcNow.AddHours(-1),
            IsDeleted = false
        };

        await dbContext.Posts.AddAsync(post);
        await dbContext.SaveChangesAsync();

        var userResourceService = new Mock<IUserResourceService>(MockBehavior.Strict);
        userResourceService
            .Setup(service => service.DeleteResourcesAsync(
                userId,
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(resourceId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(1));

        var aiFeedPostService = new Mock<IAiFeedPostService>(MockBehavior.Strict);
        var notificationService = new Mock<IFeedNotificationService>(MockBehavior.Strict);

        var handler = new DeletePostCommandHandler(
            unitOfWork,
            userResourceService.Object,
            aiFeedPostService.Object,
            notificationService.Object);

        var result = await handler.Handle(new DeletePostCommand(userId, post.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        userResourceService.VerifyAll();
        aiFeedPostService.VerifyAll();
    }

    [Fact]
    public async Task Handle_Should_DeleteAiMirrorPost_WhenFeedPostHasAiPostId()
    {
        await using var dbContext = CreateDbContext();
        using var unitOfWork = new UnitOfWork(dbContext);

        var userId = Guid.NewGuid();
        var aiPostId = Guid.NewGuid();
        var post = new Post
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Content = "Feed post",
            AiPostId = aiPostId,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            UpdatedAt = DateTime.UtcNow.AddHours(-1),
            IsDeleted = false
        };

        await dbContext.Posts.AddAsync(post);
        await dbContext.SaveChangesAsync();

        var userResourceService = new Mock<IUserResourceService>(MockBehavior.Strict);
        var aiFeedPostService = new Mock<IAiFeedPostService>(MockBehavior.Strict);
        var notificationService = new Mock<IFeedNotificationService>(MockBehavior.Strict);
        aiFeedPostService
            .Setup(service => service.DeleteMirrorPostAsync(
                It.Is<DeleteAiMirrorPostRequest>(request => request.UserId == userId && request.PostId == aiPostId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        var handler = new DeletePostCommandHandler(
            unitOfWork,
            userResourceService.Object,
            aiFeedPostService.Object,
            notificationService.Object);

        var result = await handler.Handle(new DeletePostCommand(userId, post.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        aiFeedPostService.VerifyAll();
        userResourceService.VerifyAll();
    }

    [Fact]
    public async Task Handle_Should_NotifyPostOwner_WhenAdminDeletesPost()
    {
        await using var dbContext = CreateDbContext();
        using var unitOfWork = new UnitOfWork(dbContext);

        var adminUserId = Guid.NewGuid();
        var postOwnerId = Guid.NewGuid();
        var post = new Post
        {
            Id = Guid.NewGuid(),
            UserId = postOwnerId,
            Content = "Admin removed post",
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            UpdatedAt = DateTime.UtcNow.AddHours(-1),
            IsDeleted = false
        };

        await dbContext.Posts.AddAsync(post);
        await dbContext.SaveChangesAsync();

        var userResourceService = new Mock<IUserResourceService>(MockBehavior.Strict);
        var aiFeedPostService = new Mock<IAiFeedPostService>(MockBehavior.Strict);
        var notificationService = new Mock<IFeedNotificationService>(MockBehavior.Strict);
        notificationService
            .Setup(service => service.NotifyModerationActionAsync(
                adminUserId,
                postOwnerId,
                null,
                "Post",
                post.Id,
                post.Id,
                null,
                "Resolved",
                "DeleteTargetPost",
                null,
                "Admin removed post",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new DeletePostCommandHandler(
            unitOfWork,
            userResourceService.Object,
            aiFeedPostService.Object,
            notificationService.Object);

        var result = await handler.Handle(new DeletePostCommand(adminUserId, post.Id, true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        notificationService.VerifyAll();
        userResourceService.VerifyAll();
        aiFeedPostService.VerifyAll();
    }

    private static MyDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new MyDbContext(options);
    }
}
