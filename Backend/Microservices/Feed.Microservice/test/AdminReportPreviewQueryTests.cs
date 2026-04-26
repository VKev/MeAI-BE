using Application.Abstractions.Resources;
using Application.Reports.Queries;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Context;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace test;

public sealed class AdminReportPreviewQueryTests
{
    [Fact]
    public async Task GetAdminReportPreview_Should_ReturnPostPreviewWithAuthorData()
    {
        await using var dbContext = CreateDbContext();
        using var unitOfWork = new UnitOfWork(dbContext);

        var reporterId = Guid.NewGuid();
        var postOwnerId = Guid.NewGuid();
        var now = new DateTime(2026, 4, 24, 8, 0, 0, DateTimeKind.Utc);

        var post = new Post
        {
            Id = Guid.NewGuid(),
            UserId = postOwnerId,
            Content = "Reported post content",
            MediaType = "image/jpeg",
            LikesCount = 4,
            CommentsCount = 2,
            CreatedAt = now.AddHours(-2),
            UpdatedAt = now.AddHours(-1),
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
            CreatedAt = now,
            UpdatedAt = now
        };

        await dbContext.Posts.AddAsync(post);
        await dbContext.Reports.AddAsync(report);
        await dbContext.SaveChangesAsync();

        var userResourceService = CreateUserResourceServiceMock();
        var handler = new GetAdminReportPreviewQueryHandler(unitOfWork, userResourceService.Object);

        var result = await handler.Handle(
            new GetAdminReportPreviewQuery(report.Id, null, null, 20, reporterId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Report.Id.Should().Be(report.Id);
        result.Value.Post.Should().NotBeNull();
        result.Value.Post!.Id.Should().Be(post.Id);
        result.Value.Post.Username.Should().Be($"user-{postOwnerId:N}"[..12]);
        result.Value.Post.AvatarUrl.Should().Be($"https://cdn.example.com/{postOwnerId}.jpg");
        result.Value.Comment.Should().BeNull();
    }

    [Fact]
    public async Task GetAdminReportPreview_Should_ReturnParentTargetAndPaginatedSiblingComments()
    {
        await using var dbContext = CreateDbContext();
        using var unitOfWork = new UnitOfWork(dbContext);

        var adminUserId = Guid.NewGuid();
        var postOwnerId = Guid.NewGuid();
        var parentOwnerId = Guid.NewGuid();
        var newestReplyOwnerId = Guid.NewGuid();
        var targetReplyOwnerId = Guid.NewGuid();
        var oldestReplyOwnerId = Guid.NewGuid();
        var now = new DateTime(2026, 4, 24, 12, 0, 0, DateTimeKind.Utc);

        var post = new Post
        {
            Id = Guid.NewGuid(),
            UserId = postOwnerId,
            Content = "Feed post",
            CommentsCount = 4,
            CreatedAt = now.AddHours(-3),
            UpdatedAt = now.AddHours(-3),
            IsDeleted = false
        };

        var parentComment = new Comment
        {
            Id = Guid.NewGuid(),
            PostId = post.Id,
            UserId = parentOwnerId,
            Content = "Parent comment",
            RepliesCount = 3,
            CreatedAt = now.AddHours(-2),
            UpdatedAt = now.AddHours(-2),
            IsDeleted = false
        };

        var newestReply = new Comment
        {
            Id = Guid.NewGuid(),
            PostId = post.Id,
            UserId = newestReplyOwnerId,
            ParentCommentId = parentComment.Id,
            Content = "Newest sibling reply",
            CreatedAt = now.AddMinutes(-5),
            UpdatedAt = now.AddMinutes(-5),
            IsDeleted = false
        };

        var targetReply = new Comment
        {
            Id = Guid.NewGuid(),
            PostId = post.Id,
            UserId = targetReplyOwnerId,
            ParentCommentId = parentComment.Id,
            Content = "Reported reply",
            CreatedAt = now.AddMinutes(-10),
            UpdatedAt = now.AddMinutes(-10),
            IsDeleted = false
        };

        var oldestReply = new Comment
        {
            Id = Guid.NewGuid(),
            PostId = post.Id,
            UserId = oldestReplyOwnerId,
            ParentCommentId = parentComment.Id,
            Content = "Oldest sibling reply",
            CreatedAt = now.AddMinutes(-15),
            UpdatedAt = now.AddMinutes(-15),
            IsDeleted = false
        };

        var unrelatedReply = new Comment
        {
            Id = Guid.NewGuid(),
            PostId = post.Id,
            UserId = Guid.NewGuid(),
            ParentCommentId = Guid.NewGuid(),
            Content = "Unrelated reply",
            CreatedAt = now.AddMinutes(-7),
            UpdatedAt = now.AddMinutes(-7),
            IsDeleted = false
        };

        var report = new Report
        {
            Id = Guid.NewGuid(),
            ReporterId = adminUserId,
            TargetType = "Comment",
            TargetId = targetReply.Id,
            Reason = "Harassment",
            Status = "Pending",
            ActionType = "None",
            CreatedAt = now,
            UpdatedAt = now
        };

        await dbContext.Posts.AddAsync(post);
        await dbContext.Comments.AddRangeAsync(parentComment, newestReply, targetReply, oldestReply, unrelatedReply);
        await dbContext.Reports.AddAsync(report);
        await dbContext.SaveChangesAsync();

        var userResourceService = CreateUserResourceServiceMock();
        var handler = new GetAdminReportPreviewQueryHandler(unitOfWork, userResourceService.Object);

        var firstPage = await handler.Handle(
            new GetAdminReportPreviewQuery(report.Id, null, null, 2, adminUserId),
            CancellationToken.None);

        firstPage.IsSuccess.Should().BeTrue();
        firstPage.Value.Post.Should().BeNull();
        firstPage.Value.Comment.Should().NotBeNull();
        firstPage.Value.Comment!.ParentComment.Should().NotBeNull();
        firstPage.Value.Comment.ParentComment!.Id.Should().Be(parentComment.Id);
        firstPage.Value.Comment.ParentComment.Username.Should().Be($"user-{parentOwnerId:N}"[..12]);
        firstPage.Value.Comment.TargetComment.Id.Should().Be(targetReply.Id);
        firstPage.Value.Comment.TargetComment.Content.Should().Be("Reported reply");
        firstPage.Value.Comment.TargetComment.Username.Should().Be($"user-{targetReplyOwnerId:N}"[..12]);
        firstPage.Value.Comment.Comments.Select(item => item.Id).Should().Equal(newestReply.Id, targetReply.Id);

        var lastItem = firstPage.Value.Comment.Comments[^1];

        var secondPage = await handler.Handle(
            new GetAdminReportPreviewQuery(report.Id, lastItem.CreatedAt, lastItem.Id, 2, adminUserId),
            CancellationToken.None);

        secondPage.IsSuccess.Should().BeTrue();
        secondPage.Value.Comment.Should().NotBeNull();
        secondPage.Value.Comment!.TargetComment.Id.Should().Be(targetReply.Id);
        secondPage.Value.Comment.Comments.Select(item => item.Id).Should().Equal(oldestReply.Id);
        secondPage.Value.Comment.Comments.Should().OnlyContain(item => item.ParentCommentId == parentComment.Id);
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

        userResourceService
            .Setup(service => service.GetPublicPresignedResourcesAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<UserResourcePresignResult>>(Array.Empty<UserResourcePresignResult>()));

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
