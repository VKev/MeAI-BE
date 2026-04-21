using Application.Abstractions.Notifications;
using Application.Comments.Commands;
using Application.Posts.Commands;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Context;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace test;

public sealed class LikeNotificationCommandTests
{
    [Fact]
    public async Task LikePost_Should_PublishNotification_ToPostOwner()
    {
        await using var dbContext = CreateDbContext();
        using var unitOfWork = new UnitOfWork(dbContext);

        var postOwnerId = Guid.NewGuid();
        var likerUserId = Guid.NewGuid();
        var now = new DateTime(2026, 4, 21, 10, 0, 0, DateTimeKind.Utc);
        var post = new Post
        {
            Id = Guid.NewGuid(),
            UserId = postOwnerId,
            Content = "My test post preview",
            LikesCount = 0,
            CommentsCount = 0,
            CreatedAt = now.AddMinutes(-10),
            UpdatedAt = now.AddMinutes(-10),
            IsDeleted = false
        };

        await dbContext.Posts.AddAsync(post);
        await dbContext.SaveChangesAsync();

        var notificationService = new Mock<IFeedNotificationService>();
        notificationService
            .Setup(service => service.NotifyPostLikedAsync(
                likerUserId,
                postOwnerId,
                post.Id,
                "My test post preview",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new LikePostCommandHandler(unitOfWork, notificationService.Object);

        var result = await handler.Handle(new LikePostCommand(likerUserId, post.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PostId.Should().Be(post.Id);
        result.Value.LikesCount.Should().Be(1);
        result.Value.IsLikedByCurrentUser.Should().BeTrue();

        notificationService.Verify(
            service => service.NotifyPostLikedAsync(
                likerUserId,
                postOwnerId,
                post.Id,
                "My test post preview",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task LikeComment_Should_PublishNotification_ToCommentOwner()
    {
        await using var dbContext = CreateDbContext();
        using var unitOfWork = new UnitOfWork(dbContext);

        var postOwnerId = Guid.NewGuid();
        var commentOwnerId = Guid.NewGuid();
        var likerUserId = Guid.NewGuid();
        var now = new DateTime(2026, 4, 21, 11, 0, 0, DateTimeKind.Utc);
        var post = new Post
        {
            Id = Guid.NewGuid(),
            UserId = postOwnerId,
            Content = "Parent post",
            LikesCount = 0,
            CommentsCount = 1,
            CreatedAt = now.AddMinutes(-15),
            UpdatedAt = now.AddMinutes(-15),
            IsDeleted = false
        };

        var comment = new Comment
        {
            Id = Guid.NewGuid(),
            PostId = post.Id,
            UserId = commentOwnerId,
            Content = "Comment preview content",
            LikesCount = 0,
            RepliesCount = 0,
            CreatedAt = now.AddMinutes(-5),
            UpdatedAt = now.AddMinutes(-5),
            IsDeleted = false
        };

        await dbContext.Posts.AddAsync(post);
        await dbContext.Comments.AddAsync(comment);
        await dbContext.SaveChangesAsync();

        var notificationService = new Mock<IFeedNotificationService>();
        notificationService
            .Setup(service => service.NotifyCommentLikedAsync(
                likerUserId,
                commentOwnerId,
                post.Id,
                comment.Id,
                "Comment preview content",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new LikeCommentCommandHandler(unitOfWork, notificationService.Object);

        var result = await handler.Handle(new LikeCommentCommand(likerUserId, comment.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.CommentId.Should().Be(comment.Id);
        result.Value.LikesCount.Should().Be(1);
        result.Value.IsLikedByCurrentUser.Should().BeTrue();

        notificationService.Verify(
            service => service.NotifyCommentLikedAsync(
                likerUserId,
                commentOwnerId,
                post.Id,
                comment.Id,
                "Comment preview content",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static MyDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new MyDbContext(options);
    }
}
