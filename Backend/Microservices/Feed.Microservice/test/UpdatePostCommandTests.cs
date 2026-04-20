using Application.Abstractions.Data;
using Application.Abstractions.Resources;
using Application.Common;
using Application.Posts.Commands;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Context;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace test;

public sealed class UpdatePostCommandTests
{
    [Fact]
    public async Task Handle_Should_UpdateOwnedPost_AndReconcileHashtagsAndMedia()
    {
        await using var dbContext = CreateDbContext();
        using var unitOfWork = new UnitOfWork(dbContext);

        var userId = Guid.NewGuid();
        var originalResourceId = Guid.NewGuid();
        var updatedResourceId = Guid.NewGuid();
        var now = new DateTime(2026, 4, 20, 12, 0, 0, DateTimeKind.Utc);

        var post = new Post
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Content = "Original #old",
            ResourceIds = new[] { originalResourceId },
            MediaType = "image/png",
            LikesCount = 2,
            CommentsCount = 1,
            CreatedAt = now.AddHours(-2),
            UpdatedAt = now.AddHours(-2),
            IsDeleted = false
        };

        var oldHashtag = new Hashtag
        {
            Id = Guid.NewGuid(),
            Name = "#old",
            PostCount = 1,
            CreatedAt = now.AddHours(-3)
        };

        var sharedHashtag = new Hashtag
        {
            Id = Guid.NewGuid(),
            Name = "#shared",
            PostCount = 5,
            CreatedAt = now.AddHours(-3)
        };

        await dbContext.Posts.AddAsync(post);
        await dbContext.Hashtags.AddRangeAsync(oldHashtag, sharedHashtag);
        await dbContext.PostHashtags.AddRangeAsync(
            new PostHashtag
            {
                Id = Guid.NewGuid(),
                PostId = post.Id,
                HashtagId = oldHashtag.Id,
                CreatedAt = now.AddHours(-2)
            },
            new PostHashtag
            {
                Id = Guid.NewGuid(),
                PostId = post.Id,
                HashtagId = sharedHashtag.Id,
                CreatedAt = now.AddHours(-2)
            });
        await dbContext.SaveChangesAsync();

        var userResourceService = new Mock<IUserResourceService>();
        userResourceService
            .Setup(service => service.GetPresignedResourcesAsync(
                userId,
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(updatedResourceId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<UserResourcePresignResult>>(
                new[]
                {
                    new UserResourcePresignResult(updatedResourceId, "https://cdn.example.com/updated.jpg", "image/jpeg", "image")
                }));

        userResourceService
            .Setup(service => service.GetPublicPresignedResourcesAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(updatedResourceId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<UserResourcePresignResult>>(
                new[]
                {
                    new UserResourcePresignResult(updatedResourceId, "https://cdn.example.com/updated.jpg", "image/jpeg", "image")
                }));

        userResourceService
            .Setup(service => service.GetPublicUserProfilesByIdsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(userId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyDictionary<Guid, PublicUserProfileResult>>(
                new Dictionary<Guid, PublicUserProfileResult>
                {
                    [userId] = new(userId, "owner", null, "https://cdn.example.com/avatar.jpg")
                }));

        var handler = new UpdatePostCommandHandler(unitOfWork, userResourceService.Object);

        var result = await handler.Handle(
            new UpdatePostCommand(userId, post.Id, "Updated #shared #new", new[] { updatedResourceId }, "image/jpeg"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var savedPost = await dbContext.Posts.SingleAsync();
        savedPost.Content.Should().Be("Updated #shared #new");
        savedPost.MediaType.Should().Be("image/jpeg");
        savedPost.ResourceIds.Should().ContainSingle().Which.Should().Be(updatedResourceId);
        savedPost.UpdatedAt.Should().NotBe(now.AddHours(-2));

        var hashtagLinks = await dbContext.PostHashtags
            .AsNoTracking()
            .Where(item => item.PostId == post.Id)
            .ToListAsync();
        hashtagLinks.Should().HaveCount(2);

        var hashtags = await dbContext.Hashtags
            .AsNoTracking()
            .OrderBy(item => item.Name)
            .ToListAsync();
        hashtags.Should().ContainSingle(item => item.Name == "#old" && item.PostCount == 0);
        hashtags.Should().ContainSingle(item => item.Name == "#shared" && item.PostCount == 5);
        hashtags.Should().ContainSingle(item => item.Name == "#new" && item.PostCount == 1);

        result.Value.Content.Should().Be("Updated #shared #new");
        result.Value.Media.Should().ContainSingle(item => item.ResourceId == updatedResourceId);
        result.Value.Hashtags.Should().BeEquivalentTo(new[] { "#new", "#shared" });
        result.Value.CanDelete.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Should_ReturnForbidden_WhenEditingAnotherUsersPost()
    {
        await using var dbContext = CreateDbContext();
        using var unitOfWork = new UnitOfWork(dbContext);

        var post = new Post
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Content = "Original",
            ResourceIds = Array.Empty<Guid>(),
            MediaType = null,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5),
            IsDeleted = false
        };

        await dbContext.Posts.AddAsync(post);
        await dbContext.SaveChangesAsync();

        var userResourceService = new Mock<IUserResourceService>(MockBehavior.Strict);
        var handler = new UpdatePostCommandHandler(unitOfWork, userResourceService.Object);

        var result = await handler.Handle(
            new UpdatePostCommand(Guid.NewGuid(), post.Id, "Updated", null, null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(FeedErrors.Forbidden);
    }

    [Fact]
    public async Task Handle_Should_ReturnMissingResources_WhenResolvedCountDoesNotMatch()
    {
        await using var dbContext = CreateDbContext();
        using var unitOfWork = new UnitOfWork(dbContext);

        var userId = Guid.NewGuid();
        var requestedResourceId = Guid.NewGuid();
        var post = new Post
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Content = "Original",
            ResourceIds = Array.Empty<Guid>(),
            MediaType = null,
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-10),
            IsDeleted = false
        };

        await dbContext.Posts.AddAsync(post);
        await dbContext.SaveChangesAsync();

        var userResourceService = new Mock<IUserResourceService>();
        userResourceService
            .Setup(service => service.GetPresignedResourcesAsync(
                userId,
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<UserResourcePresignResult>>(Array.Empty<UserResourcePresignResult>()));

        var handler = new UpdatePostCommandHandler(unitOfWork, userResourceService.Object);

        var result = await handler.Handle(
            new UpdatePostCommand(userId, post.Id, "Updated", new[] { requestedResourceId }, "image/jpeg"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Feed.Resource.Missing");
    }

    private static MyDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new MyDbContext(options);
    }
}
