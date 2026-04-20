using Application.Abstractions.Notifications;
using Application.Abstractions.Resources;
using Application.Common;
using Application.Comments.Commands;
using Application.Comments.Queries;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Context;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace test;

public sealed class CommentOwnershipTests
{
    [Fact]
    public async Task DeleteComment_Should_AllowCommentOwnerToDeleteOwnThread_OnAnotherUsersPost()
    {
        await using var dbContext = CreateDbContext();
        using var unitOfWork = new UnitOfWork(dbContext);

        var postOwnerId = Guid.NewGuid();
        var commentOwnerId = Guid.NewGuid();
        var replyOwnerId = Guid.NewGuid();
        var now = new DateTime(2026, 4, 20, 15, 0, 0, DateTimeKind.Utc);

        var post = new Post
        {
            Id = Guid.NewGuid(),
            UserId = postOwnerId,
            Content = "Feed post",
            CommentsCount = 2,
            CreatedAt = now.AddHours(-2),
            UpdatedAt = now.AddHours(-2),
            IsDeleted = false
        };

        var rootComment = new Comment
        {
            Id = Guid.NewGuid(),
            PostId = post.Id,
            UserId = commentOwnerId,
            Content = "Root comment",
            RepliesCount = 1,
            CreatedAt = now.AddHours(-1),
            UpdatedAt = now.AddHours(-1),
            IsDeleted = false
        };

        var reply = new Comment
        {
            Id = Guid.NewGuid(),
            PostId = post.Id,
            UserId = replyOwnerId,
            ParentCommentId = rootComment.Id,
            Content = "Reply",
            CreatedAt = now.AddMinutes(-30),
            UpdatedAt = now.AddMinutes(-30),
            IsDeleted = false
        };

        await dbContext.Posts.AddAsync(post);
        await dbContext.Comments.AddRangeAsync(rootComment, reply);
        await dbContext.SaveChangesAsync();

        var handler = new DeleteCommentCommandHandler(unitOfWork);

        var result = await handler.Handle(new DeleteCommentCommand(commentOwnerId, rootComment.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var savedPost = await dbContext.Posts.SingleAsync();
        var savedComments = await dbContext.Comments.OrderBy(item => item.CreatedAt).ToListAsync();

        savedPost.CommentsCount.Should().Be(0);
        savedComments.Should().OnlyContain(item => item.IsDeleted);
        savedComments.Should().OnlyContain(item => item.DeletedAt.HasValue);
    }

    [Fact]
    public async Task GetCommentsByPostId_Should_SetCanDeleteTrueOnlyForOwnedComments_WhenViewerIsNotPostOwner()
    {
        await using var dbContext = CreateDbContext();
        using var unitOfWork = new UnitOfWork(dbContext);

        var postOwnerId = Guid.NewGuid();
        var viewerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var now = new DateTime(2026, 4, 20, 16, 0, 0, DateTimeKind.Utc);

        var post = new Post
        {
            Id = Guid.NewGuid(),
            UserId = postOwnerId,
            Content = "Feed post",
            CommentsCount = 2,
            CreatedAt = now.AddHours(-2),
            UpdatedAt = now.AddHours(-2),
            IsDeleted = false
        };

        var ownedComment = new Comment
        {
            Id = Guid.NewGuid(),
            PostId = post.Id,
            UserId = viewerId,
            Content = "My comment",
            CreatedAt = now.AddMinutes(-20),
            UpdatedAt = now.AddMinutes(-20),
            IsDeleted = false
        };

        var otherComment = new Comment
        {
            Id = Guid.NewGuid(),
            PostId = post.Id,
            UserId = otherUserId,
            Content = "Other comment",
            CreatedAt = now.AddMinutes(-10),
            UpdatedAt = now.AddMinutes(-10),
            IsDeleted = false
        };

        await dbContext.Posts.AddAsync(post);
        await dbContext.Comments.AddRangeAsync(ownedComment, otherComment);
        await dbContext.SaveChangesAsync();

        var userResourceService = CreateUserResourceServiceMock();
        var handler = new GetCommentsByPostIdQueryHandler(unitOfWork, userResourceService.Object);

        var result = await handler.Handle(new GetCommentsByPostIdQuery(post.Id, null, null, 20, viewerId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Should().ContainSingle(item => item.Id == ownedComment.Id && item.CanDelete == true);
        result.Value.Should().ContainSingle(item => item.Id == otherComment.Id && item.CanDelete == false);
    }

    [Fact]
    public async Task GetCommentReplies_Should_SetCanDeleteTrueOnlyForOwnedReplies_WhenViewerIsNotPostOwner()
    {
        await using var dbContext = CreateDbContext();
        using var unitOfWork = new UnitOfWork(dbContext);

        var postOwnerId = Guid.NewGuid();
        var parentOwnerId = Guid.NewGuid();
        var viewerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var now = new DateTime(2026, 4, 20, 17, 0, 0, DateTimeKind.Utc);

        var post = new Post
        {
            Id = Guid.NewGuid(),
            UserId = postOwnerId,
            Content = "Feed post",
            CommentsCount = 3,
            CreatedAt = now.AddHours(-2),
            UpdatedAt = now.AddHours(-2),
            IsDeleted = false
        };

        var parentComment = new Comment
        {
            Id = Guid.NewGuid(),
            PostId = post.Id,
            UserId = parentOwnerId,
            Content = "Root comment",
            RepliesCount = 2,
            CreatedAt = now.AddHours(-1),
            UpdatedAt = now.AddHours(-1),
            IsDeleted = false
        };

        var ownedReply = new Comment
        {
            Id = Guid.NewGuid(),
            PostId = post.Id,
            UserId = viewerId,
            ParentCommentId = parentComment.Id,
            Content = "My reply",
            CreatedAt = now.AddMinutes(-15),
            UpdatedAt = now.AddMinutes(-15),
            IsDeleted = false
        };

        var otherReply = new Comment
        {
            Id = Guid.NewGuid(),
            PostId = post.Id,
            UserId = otherUserId,
            ParentCommentId = parentComment.Id,
            Content = "Other reply",
            CreatedAt = now.AddMinutes(-5),
            UpdatedAt = now.AddMinutes(-5),
            IsDeleted = false
        };

        await dbContext.Posts.AddAsync(post);
        await dbContext.Comments.AddRangeAsync(parentComment, ownedReply, otherReply);
        await dbContext.SaveChangesAsync();

        var userResourceService = CreateUserResourceServiceMock();
        var handler = new GetCommentRepliesQueryHandler(unitOfWork, userResourceService.Object);

        var result = await handler.Handle(new GetCommentRepliesQuery(parentComment.Id, null, null, 20, viewerId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Should().ContainSingle(item => item.Id == ownedReply.Id && item.CanDelete == true);
        result.Value.Should().ContainSingle(item => item.Id == otherReply.Id && item.CanDelete == false);
    }

    [Fact]
    public async Task CreateComment_Should_ReturnCanDeleteTrueForCreator_EvenWhenViewerDoesNotOwnPost()
    {
        await using var dbContext = CreateDbContext();
        using var unitOfWork = new UnitOfWork(dbContext);

        var postOwnerId = Guid.NewGuid();
        var commenterId = Guid.NewGuid();
        var now = new DateTime(2026, 4, 20, 18, 0, 0, DateTimeKind.Utc);

        var post = new Post
        {
            Id = Guid.NewGuid(),
            UserId = postOwnerId,
            Content = "Feed post",
            CommentsCount = 0,
            CreatedAt = now.AddHours(-1),
            UpdatedAt = now.AddHours(-1),
            IsDeleted = false
        };

        await dbContext.Posts.AddAsync(post);
        await dbContext.SaveChangesAsync();

        var userResourceService = CreateUserResourceServiceMock();
        var notificationService = new Mock<IFeedNotificationService>();
        notificationService
            .Setup(service => service.NotifyCommentAsync(
                commenterId,
                postOwnerId,
                post.Id,
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new CreateCommentCommandHandler(unitOfWork, notificationService.Object, userResourceService.Object);

        var result = await handler.Handle(new CreateCommentCommand(commenterId, post.Id, "My new comment"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.CanDelete.Should().BeTrue();
    }

    [Fact]
    public async Task ReplyToComment_Should_ReturnCanDeleteTrueForCreator_EvenWhenViewerDoesNotOwnPost()
    {
        await using var dbContext = CreateDbContext();
        using var unitOfWork = new UnitOfWork(dbContext);

        var postOwnerId = Guid.NewGuid();
        var parentOwnerId = Guid.NewGuid();
        var replierId = Guid.NewGuid();
        var now = new DateTime(2026, 4, 20, 19, 0, 0, DateTimeKind.Utc);

        var post = new Post
        {
            Id = Guid.NewGuid(),
            UserId = postOwnerId,
            Content = "Feed post",
            CommentsCount = 1,
            CreatedAt = now.AddHours(-1),
            UpdatedAt = now.AddHours(-1),
            IsDeleted = false
        };

        var parentComment = new Comment
        {
            Id = Guid.NewGuid(),
            PostId = post.Id,
            UserId = parentOwnerId,
            Content = "Parent comment",
            RepliesCount = 0,
            CreatedAt = now.AddMinutes(-45),
            UpdatedAt = now.AddMinutes(-45),
            IsDeleted = false
        };

        await dbContext.Posts.AddAsync(post);
        await dbContext.Comments.AddAsync(parentComment);
        await dbContext.SaveChangesAsync();

        var userResourceService = CreateUserResourceServiceMock();
        var notificationService = new Mock<IFeedNotificationService>();
        notificationService
            .Setup(service => service.NotifyCommentAsync(
                replierId,
                postOwnerId,
                post.Id,
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new ReplyToCommentCommandHandler(unitOfWork, notificationService.Object, userResourceService.Object);

        var result = await handler.Handle(new ReplyToCommentCommand(replierId, parentComment.Id, "My reply"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.CanDelete.Should().BeTrue();
    }

    private static Mock<IUserResourceService> CreateUserResourceServiceMock()
    {
        var userResourceService = new Mock<IUserResourceService>();
        userResourceService
            .Setup(service => service.GetPublicUserProfilesByIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> userIds, CancellationToken _) =>
            {
                var profiles = userIds
                    .Distinct()
                    .ToDictionary(
                        id => id,
                        id => new PublicUserProfileResult(
                            id,
                            $"user-{id:N}"[..12],
                            null,
                            $"https://cdn.example.com/{id}.jpg"));

                return Result.Success<IReadOnlyDictionary<Guid, PublicUserProfileResult>>(profiles);
            });

        return userResourceService;
    }

    private static MyDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new MyDbContext(options);
    }
}
