using Application.Abstractions.Data;
using Application.Abstractions.Resources;
using Application.Follows.Models;
using Application.Follows.Queries;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Context;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace test;

public sealed class GetFollowSuggestionsQueryTests
{
    [Fact]
    public async Task Handle_Should_ReturnUsersNotFollowed_OrderedByPostCountThenLatestPost()
    {
        await using var dbContext = CreateDbContext();

        var currentUserId = Guid.NewGuid();
        var topCandidateId = Guid.NewGuid();
        var secondCandidateId = Guid.NewGuid();
        var followedUserId = Guid.NewGuid();
        var deletedOnlyUserId = Guid.NewGuid();

        SeedFollow(dbContext, currentUserId, followedUserId);

        SeedPost(dbContext, topCandidateId, new DateTime(2026, 4, 19, 12, 0, 0, DateTimeKind.Utc));
        SeedPost(dbContext, topCandidateId, new DateTime(2026, 4, 19, 11, 0, 0, DateTimeKind.Utc));
        SeedPost(dbContext, topCandidateId, new DateTime(2026, 4, 19, 10, 0, 0, DateTimeKind.Utc));

        SeedPost(dbContext, secondCandidateId, new DateTime(2026, 4, 19, 9, 0, 0, DateTimeKind.Utc));
        SeedPost(dbContext, secondCandidateId, new DateTime(2026, 4, 18, 9, 0, 0, DateTimeKind.Utc));

        SeedPost(dbContext, followedUserId, new DateTime(2026, 4, 19, 13, 0, 0, DateTimeKind.Utc));
        SeedPost(dbContext, followedUserId, new DateTime(2026, 4, 18, 13, 0, 0, DateTimeKind.Utc));
        SeedPost(dbContext, followedUserId, new DateTime(2026, 4, 17, 13, 0, 0, DateTimeKind.Utc));
        SeedDeletedPost(dbContext, deletedOnlyUserId, new DateTime(2026, 4, 19, 14, 0, 0, DateTimeKind.Utc));
        SeedPost(dbContext, currentUserId, new DateTime(2026, 4, 19, 8, 0, 0, DateTimeKind.Utc));

        await dbContext.SaveChangesAsync();

        using var unitOfWork = new UnitOfWork(dbContext);
        var userResourceService = new Mock<IUserResourceService>();
        userResourceService
            .Setup(item => item.GetPublicUserProfilesByIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> userIds, CancellationToken _) =>
            {
                var profiles = userIds.ToDictionary(
                    id => id,
                    id => new PublicUserProfileResult(
                        id,
                        id == topCandidateId ? "top-user" : "second-user",
                        id == topCandidateId ? "Top User" : "Second User",
                        $"https://cdn.example.com/{id}.jpg"));

                return Result.Success<IReadOnlyDictionary<Guid, PublicUserProfileResult>>(profiles);
            });

        var handler = new GetFollowSuggestionsQueryHandler(unitOfWork, userResourceService.Object);

        var result = await handler.Handle(new GetFollowSuggestionsQuery(currentUserId, 10), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Select(item => item.UserId).Should().Equal(topCandidateId, secondCandidateId);
        result.Value[0].PostCount.Should().Be(3);
        result.Value[0].Username.Should().Be("top-user");
        result.Value[1].PostCount.Should().Be(2);
        result.Value.Should().NotContain(item => item.UserId == currentUserId || item.UserId == followedUserId || item.UserId == deletedOnlyUserId);
    }

    [Fact]
    public async Task Handle_Should_RespectLimit_AndSkipProfilesMissingFromLookup()
    {
        await using var dbContext = CreateDbContext();

        var currentUserId = Guid.NewGuid();
        var firstCandidateId = Guid.NewGuid();
        var missingProfileId = Guid.NewGuid();
        var thirdCandidateId = Guid.NewGuid();

        SeedPost(dbContext, firstCandidateId, new DateTime(2026, 4, 19, 12, 0, 0, DateTimeKind.Utc));
        SeedPost(dbContext, firstCandidateId, new DateTime(2026, 4, 18, 12, 0, 0, DateTimeKind.Utc));
        SeedPost(dbContext, missingProfileId, new DateTime(2026, 4, 19, 11, 0, 0, DateTimeKind.Utc));
        SeedPost(dbContext, missingProfileId, new DateTime(2026, 4, 18, 11, 0, 0, DateTimeKind.Utc));
        SeedPost(dbContext, thirdCandidateId, new DateTime(2026, 4, 17, 11, 0, 0, DateTimeKind.Utc));

        await dbContext.SaveChangesAsync();

        using var unitOfWork = new UnitOfWork(dbContext);
        var userResourceService = new Mock<IUserResourceService>();
        userResourceService
            .Setup(item => item.GetPublicUserProfilesByIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> userIds, CancellationToken _) =>
            {
                var profiles = userIds
                    .Where(id => id != missingProfileId)
                    .ToDictionary(
                        id => id,
                        id => new PublicUserProfileResult(
                            id,
                            id == firstCandidateId ? "first-user" : "third-user",
                            id == firstCandidateId ? "First User" : "Third User",
                            null));

                return Result.Success<IReadOnlyDictionary<Guid, PublicUserProfileResult>>(profiles);
            });

        var handler = new GetFollowSuggestionsQueryHandler(unitOfWork, userResourceService.Object);

        var result = await handler.Handle(new GetFollowSuggestionsQuery(currentUserId, 2), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].UserId.Should().Be(firstCandidateId);
        result.Value[0].PostCount.Should().Be(2);
        result.Value.Should().NotContain(item => item.UserId == missingProfileId || item.UserId == thirdCandidateId);
    }

    private static MyDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new MyDbContext(options);
    }

    private static void SeedFollow(MyDbContext dbContext, Guid followerId, Guid followeeId)
    {
        dbContext.Follows.Add(new Follow
        {
            Id = Guid.NewGuid(),
            FollowerId = followerId,
            FolloweeId = followeeId,
            CreatedAt = DateTime.UtcNow
        });
    }

    private static void SeedPost(MyDbContext dbContext, Guid userId, DateTime createdAt)
    {
        dbContext.Posts.Add(new Post
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Content = "post",
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            DeletedAt = null,
            IsDeleted = false
        });
    }

    private static void SeedDeletedPost(MyDbContext dbContext, Guid userId, DateTime createdAt)
    {
        dbContext.Posts.Add(new Post
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Content = "deleted post",
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            DeletedAt = createdAt,
            IsDeleted = true
        });
    }
}
