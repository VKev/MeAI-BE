using Application.Abstractions.Notifications;
using Application.Reports.Commands;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Context;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace test;

public sealed class AdminReportModerationCommandTests
{
    [Fact]
    public async Task ReviewReport_DeleteTargetPost_Should_SoftDeletePostAndNotifyOwner()
    {
        await using var dbContext = CreateDbContext();
        using var unitOfWork = new UnitOfWork(dbContext);

        var adminUserId = Guid.NewGuid();
        var reporterId = Guid.NewGuid();
        var postOwnerId = Guid.NewGuid();
        var now = new DateTime(2026, 5, 25, 10, 0, 0, DateTimeKind.Utc);

        var post = new Post
        {
            Id = Guid.NewGuid(),
            UserId = postOwnerId,
            Content = "Reported post content",
            CommentsCount = 0,
            CreatedAt = now.AddHours(-2),
            UpdatedAt = now.AddHours(-2),
            IsDeleted = false
        };

        var report = new Report
        {
            Id = Guid.NewGuid(),
            ReporterId = reporterId,
            TargetType = "Post",
            TargetId = post.Id,
            Reason = "Spam",
            Status = "Pending",
            ActionType = "None",
            CreatedAt = now.AddHours(-1),
            UpdatedAt = now.AddHours(-1)
        };

        await dbContext.Posts.AddAsync(post);
        await dbContext.Reports.AddAsync(report);
        await dbContext.SaveChangesAsync();

        var notificationService = new Mock<IFeedNotificationService>();
        notificationService
            .Setup(service => service.NotifyModerationActionAsync(
                adminUserId,
                postOwnerId,
                report.Id,
                "Post",
                post.Id,
                post.Id,
                null,
                "Resolved",
                "DeleteTargetPost",
                "Removed violating post",
                "Reported post content",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new ReviewReportCommandHandler(unitOfWork, notificationService.Object);

        var result = await handler.Handle(
            new ReviewReportCommand(adminUserId, report.Id, "Resolved", "DeleteTargetPost", "Removed violating post"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ActionType.Should().Be("DeleteTargetPost");

        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var savedPost = await dbContext.Posts.SingleAsync();
        savedPost.IsDeleted.Should().BeTrue();
        savedPost.DeletedAt.Should().NotBeNull();

        notificationService.Verify(
            service => service.NotifyModerationActionAsync(
                adminUserId,
                postOwnerId,
                report.Id,
                "Post",
                post.Id,
                post.Id,
                null,
                "Resolved",
                "DeleteTargetPost",
                "Removed violating post",
                "Reported post content",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ReviewReport_DeleteTargetComment_Should_SoftDeleteCommentThreadAndNotifyCommentOwner()
    {
        await using var dbContext = CreateDbContext();
        using var unitOfWork = new UnitOfWork(dbContext);

        var adminUserId = Guid.NewGuid();
        var reporterId = Guid.NewGuid();
        var postOwnerId = Guid.NewGuid();
        var commentOwnerId = Guid.NewGuid();
        var replyOwnerId = Guid.NewGuid();
        var now = new DateTime(2026, 5, 25, 11, 0, 0, DateTimeKind.Utc);

        var post = new Post
        {
            Id = Guid.NewGuid(),
            UserId = postOwnerId,
            Content = "Parent post",
            CommentsCount = 2,
            CreatedAt = now.AddHours(-3),
            UpdatedAt = now.AddHours(-3),
            IsDeleted = false
        };

        var comment = new Comment
        {
            Id = Guid.NewGuid(),
            PostId = post.Id,
            UserId = commentOwnerId,
            Content = "Reported comment content",
            RepliesCount = 1,
            CreatedAt = now.AddHours(-2),
            UpdatedAt = now.AddHours(-2),
            IsDeleted = false
        };

        var reply = new Comment
        {
            Id = Guid.NewGuid(),
            PostId = post.Id,
            UserId = replyOwnerId,
            ParentCommentId = comment.Id,
            Content = "Reply content",
            CreatedAt = now.AddHours(-1),
            UpdatedAt = now.AddHours(-1),
            IsDeleted = false
        };

        var report = new Report
        {
            Id = Guid.NewGuid(),
            ReporterId = reporterId,
            TargetType = "Comment",
            TargetId = comment.Id,
            Reason = "Harassment",
            Status = "Pending",
            ActionType = "None",
            CreatedAt = now.AddMinutes(-30),
            UpdatedAt = now.AddMinutes(-30)
        };

        await dbContext.Posts.AddAsync(post);
        await dbContext.Comments.AddRangeAsync(comment, reply);
        await dbContext.Reports.AddAsync(report);
        await dbContext.SaveChangesAsync();

        var notificationService = new Mock<IFeedNotificationService>();
        notificationService
            .Setup(service => service.NotifyModerationActionAsync(
                adminUserId,
                commentOwnerId,
                report.Id,
                "Comment",
                comment.Id,
                post.Id,
                comment.Id,
                "Resolved",
                "DeleteTargetComment",
                "Removed violating comment",
                "Reported comment content",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new ReviewReportCommandHandler(unitOfWork, notificationService.Object);

        var result = await handler.Handle(
            new ReviewReportCommand(adminUserId, report.Id, "Resolved", "DeleteTargetComment", "Removed violating comment"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ActionType.Should().Be("DeleteTargetComment");

        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var savedPost = await dbContext.Posts.SingleAsync();
        var savedComments = await dbContext.Comments.OrderBy(item => item.CreatedAt).ToListAsync();

        savedPost.CommentsCount.Should().Be(0);
        savedComments.Should().OnlyContain(item => item.IsDeleted);
        savedComments.Should().OnlyContain(item => item.DeletedAt.HasValue);

        notificationService.Verify(
            service => service.NotifyModerationActionAsync(
                adminUserId,
                commentOwnerId,
                report.Id,
                "Comment",
                comment.Id,
                post.Id,
                comment.Id,
                "Resolved",
                "DeleteTargetComment",
                "Removed violating comment",
                "Reported comment content",
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
