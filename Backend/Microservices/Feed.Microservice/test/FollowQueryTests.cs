using Application.Abstractions.Data;
using Application.Abstractions.Notifications;
using Application.Abstractions.Resources;
using Application.Common;
using Application.Follows;
using Application.Follows.Commands;
using Application.Follows.Models;
using Application.Follows.Queries;
using Application.Profiles.Models;
using Application.Profiles.Queries;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Context;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace test;

public sealed class FollowQueryTests
{
    [Fact]
    public async Task GetFollowers_Should_ReturnPaginatedProfiles_WithPostCounts()
    {
        await using var dbContext = CreateDbContext();

        var targetUserId = Guid.NewGuid();
        var followerOneId = Guid.NewGuid();
        var followerTwoId = Guid.NewGuid();
        var followerThreeId = Guid.NewGuid();

        var firstCreatedAt = new DateTime(2026, 4, 19, 12, 0, 0, DateTimeKind.Utc);
        var secondCreatedAt = new DateTime(2026, 4, 19, 11, 0, 0, DateTimeKind.Utc);
        var thirdCreatedAt = new DateTime(2026, 4, 19, 10, 0, 0, DateTimeKind.Utc);

        SeedFollow(dbContext, Guid.Parse("00000000-0000-0000-0000-000000000301"), followerOneId, targetUserId, firstCreatedAt);
        SeedFollow(dbContext, Guid.Parse("00000000-0000-0000-0000-000000000302"), followerTwoId, targetUserId, secondCreatedAt);
        SeedFollow(dbContext, Guid.Parse("00000000-0000-0000-0000-000000000303"), followerThreeId, targetUserId, thirdCreatedAt);

        SeedPost(dbContext, followerOneId, firstCreatedAt.AddMinutes(-1));
        SeedPost(dbContext, followerOneId, firstCreatedAt.AddMinutes(-2));
        SeedPost(dbContext, followerTwoId, secondCreatedAt.AddMinutes(-1));
        SeedDeletedPost(dbContext, followerThreeId, thirdCreatedAt.AddMinutes(-1));

        await dbContext.SaveChangesAsync();

        using var unitOfWork = new UnitOfWork(dbContext);
        var userResourceService = CreateUserResourceServiceMock(new Dictionary<Guid, PublicUserProfileResult>
        {
            [followerOneId] = new(followerOneId, "follower-one", "Follower One", "https://cdn.example.com/one.jpg"),
            [followerTwoId] = new(followerTwoId, "follower-two", "Follower Two", "https://cdn.example.com/two.jpg"),
            [followerThreeId] = new(followerThreeId, "follower-three", "Follower Three", null)
        });

        var handler = new GetFollowersQueryHandler(unitOfWork, userResourceService.Object);

        var result = await handler.Handle(new GetFollowersQuery(targetUserId, null, null, 2), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Select(item => item.UserId).Should().Equal(followerOneId, followerTwoId);
        result.Value[0].FollowId.Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000301"));
        result.Value[0].Username.Should().Be("follower-one");
        result.Value[0].FullName.Should().Be("Follower One");
        result.Value[0].AvatarUrl.Should().Be("https://cdn.example.com/one.jpg");
        result.Value[0].PostCount.Should().Be(2);
        result.Value[1].PostCount.Should().Be(1);
    }

    [Fact]
    public async Task GetFollowers_Should_ApplyCursor_AndSkipProfilesMissingFromLookup()
    {
        await using var dbContext = CreateDbContext();

        var targetUserId = Guid.NewGuid();
        var firstFollowerId = Guid.NewGuid();
        var secondFollowerId = Guid.NewGuid();
        var thirdFollowerId = Guid.NewGuid();

        var firstFollowId = Guid.Parse("00000000-0000-0000-0000-000000000401");
        var secondFollowId = Guid.Parse("00000000-0000-0000-0000-000000000402");
        var thirdFollowId = Guid.Parse("00000000-0000-0000-0000-000000000403");

        var firstCreatedAt = new DateTime(2026, 4, 19, 12, 0, 0, DateTimeKind.Utc);
        var secondCreatedAt = new DateTime(2026, 4, 19, 11, 0, 0, DateTimeKind.Utc);
        var thirdCreatedAt = new DateTime(2026, 4, 19, 10, 0, 0, DateTimeKind.Utc);

        SeedFollow(dbContext, firstFollowId, firstFollowerId, targetUserId, firstCreatedAt);
        SeedFollow(dbContext, secondFollowId, secondFollowerId, targetUserId, secondCreatedAt);
        SeedFollow(dbContext, thirdFollowId, thirdFollowerId, targetUserId, thirdCreatedAt);

        await dbContext.SaveChangesAsync();

        using var unitOfWork = new UnitOfWork(dbContext);
        var userResourceService = CreateUserResourceServiceMock(new Dictionary<Guid, PublicUserProfileResult>
        {
            [firstFollowerId] = new(firstFollowerId, "first-follower", "First Follower", null),
            [thirdFollowerId] = new(thirdFollowerId, "third-follower", "Third Follower", null)
        });

        var handler = new GetFollowersQueryHandler(unitOfWork, userResourceService.Object);

        var result = await handler.Handle(
            new GetFollowersQuery(targetUserId, firstCreatedAt, firstFollowId, 2),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].UserId.Should().Be(thirdFollowerId);
        result.Value[0].FollowId.Should().Be(thirdFollowId);
    }

    [Fact]
    public async Task GetFollowing_Should_ReturnPaginatedProfiles_WithPostCounts()
    {
        await using var dbContext = CreateDbContext();

        var currentUserId = Guid.NewGuid();
        var followeeOneId = Guid.NewGuid();
        var followeeTwoId = Guid.NewGuid();
        var followeeThreeId = Guid.NewGuid();

        var firstCreatedAt = new DateTime(2026, 4, 19, 12, 0, 0, DateTimeKind.Utc);
        var secondCreatedAt = new DateTime(2026, 4, 19, 11, 0, 0, DateTimeKind.Utc);
        var thirdCreatedAt = new DateTime(2026, 4, 19, 10, 0, 0, DateTimeKind.Utc);

        SeedFollow(dbContext, Guid.Parse("00000000-0000-0000-0000-000000000501"), currentUserId, followeeOneId, firstCreatedAt);
        SeedFollow(dbContext, Guid.Parse("00000000-0000-0000-0000-000000000502"), currentUserId, followeeTwoId, secondCreatedAt);
        SeedFollow(dbContext, Guid.Parse("00000000-0000-0000-0000-000000000503"), currentUserId, followeeThreeId, thirdCreatedAt);

        SeedPost(dbContext, followeeOneId, firstCreatedAt.AddMinutes(-1));
        SeedPost(dbContext, followeeTwoId, secondCreatedAt.AddMinutes(-1));
        SeedPost(dbContext, followeeTwoId, secondCreatedAt.AddMinutes(-2));

        await dbContext.SaveChangesAsync();

        using var unitOfWork = new UnitOfWork(dbContext);
        var userResourceService = CreateUserResourceServiceMock(new Dictionary<Guid, PublicUserProfileResult>
        {
            [followeeOneId] = new(followeeOneId, "followee-one", "Followee One", "https://cdn.example.com/a.jpg"),
            [followeeTwoId] = new(followeeTwoId, "followee-two", "Followee Two", "https://cdn.example.com/b.jpg"),
            [followeeThreeId] = new(followeeThreeId, "followee-three", "Followee Three", null)
        });

        var handler = new GetFollowingQueryHandler(unitOfWork, userResourceService.Object);

        var result = await handler.Handle(new GetFollowingQuery(currentUserId, null, null, 2), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Select(item => item.UserId).Should().Equal(followeeOneId, followeeTwoId);
        result.Value[0].PostCount.Should().Be(1);
        result.Value[1].PostCount.Should().Be(2);
    }

    [Fact]
    public async Task FollowUser_Should_ReturnEnrichedFolloweeProfile_WithPostCount()
    {
        await using var dbContext = CreateDbContext();
        using var unitOfWork = new UnitOfWork(dbContext);

        var followerId = Guid.NewGuid();
        var followeeId = Guid.NewGuid();
        var existingPostCreatedAt = new DateTime(2026, 4, 19, 9, 0, 0, DateTimeKind.Utc);

        SeedPost(dbContext, followeeId, existingPostCreatedAt);
        SeedPost(dbContext, followeeId, existingPostCreatedAt.AddMinutes(-10));
        await dbContext.SaveChangesAsync();

        var notificationService = new Mock<IFeedNotificationService>();
        notificationService
            .Setup(service => service.NotifyFollowedAsync(followerId, followeeId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var userResourceService = CreateUserResourceServiceMock(new Dictionary<Guid, PublicUserProfileResult>
        {
            [followeeId] = new(followeeId, "followee-user", "Followee User", "https://cdn.example.com/followee.jpg")
        });

        var handler = new FollowUserCommandHandler(unitOfWork, notificationService.Object, userResourceService.Object);

        var result = await handler.Handle(new FollowUserCommand(followerId, followeeId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(followeeId);
        result.Value.Username.Should().Be("followee-user");
        result.Value.FullName.Should().Be("Followee User");
        result.Value.AvatarUrl.Should().Be("https://cdn.example.com/followee.jpg");
        result.Value.PostCount.Should().Be(2);
        result.Value.FollowId.Should().NotBeEmpty();
        result.Value.FollowedAt.Should().NotBeNull();

        notificationService.Verify(
            service => service.NotifyFollowedAsync(followerId, followeeId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task FollowUser_Should_ReturnUserNotFound_WhenProfileLookupDoesNotContainFollowee()
    {
        await using var dbContext = CreateDbContext();
        using var unitOfWork = new UnitOfWork(dbContext);

        var followerId = Guid.NewGuid();
        var followeeId = Guid.NewGuid();
        var notificationService = new Mock<IFeedNotificationService>();
        notificationService
            .Setup(service => service.NotifyFollowedAsync(followerId, followeeId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var userResourceService = CreateUserResourceServiceMock(new Dictionary<Guid, PublicUserProfileResult>());
        var handler = new FollowUserCommandHandler(unitOfWork, notificationService.Object, userResourceService.Object);

        var result = await handler.Handle(new FollowUserCommand(followerId, followeeId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(FeedErrors.UserNotFound);
    }

    [Fact]
    public async Task BuildFollowResponses_Should_Fail_WhenProfileLookupFails()
    {
        await using var dbContext = CreateDbContext();
        using var unitOfWork = new UnitOfWork(dbContext);

        var expectedError = new Error("UserResources.GrpcError", "lookup failed");
        var userResourceService = new Mock<IUserResourceService>();
        userResourceService
            .Setup(item => item.GetPublicUserProfilesByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<IReadOnlyDictionary<Guid, PublicUserProfileResult>>(expectedError));

        var result = await FollowSupport.BuildFollowResponsesAsync(
            unitOfWork,
            userResourceService.Object,
            new[] { new FollowCandidate(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow) },
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(expectedError);
    }

    [Fact]
    public async Task GetPublicProfileByUsername_Should_ReturnCountsIncludingPostCount()
    {
        await using var dbContext = CreateDbContext();
        using var unitOfWork = new UnitOfWork(dbContext);

        var targetUserId = Guid.NewGuid();
        var followerOneId = Guid.NewGuid();
        var followerTwoId = Guid.NewGuid();
        var followingOneId = Guid.NewGuid();
        var viewerId = Guid.NewGuid();
        var now = new DateTime(2026, 4, 21, 9, 0, 0, DateTimeKind.Utc);

        SeedFollow(dbContext, Guid.NewGuid(), followerOneId, targetUserId, now.AddMinutes(-1));
        SeedFollow(dbContext, Guid.NewGuid(), followerTwoId, targetUserId, now.AddMinutes(-2));
        SeedFollow(dbContext, Guid.NewGuid(), targetUserId, followingOneId, now.AddMinutes(-3));
        SeedFollow(dbContext, Guid.NewGuid(), viewerId, targetUserId, now.AddMinutes(-4));

        SeedPost(dbContext, targetUserId, now.AddMinutes(-5));
        SeedPost(dbContext, targetUserId, now.AddMinutes(-6));
        SeedDeletedPost(dbContext, targetUserId, now.AddMinutes(-7));
        await dbContext.SaveChangesAsync();

        var userResourceService = new Mock<IUserResourceService>();
        userResourceService
            .Setup(service => service.GetPublicUserProfileByUsernameAsync("feed-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PublicUserProfileResult(
                targetUserId,
                "feed-user",
                "Feed User",
                "https://cdn.example.com/feed-user.jpg")));

        var handler = new GetPublicProfileByUsernameQueryHandler(unitOfWork, userResourceService.Object);

        var result = await handler.Handle(new GetPublicProfileByUsernameQuery("feed-user", viewerId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new PublicProfileResponse(
            targetUserId,
            "feed-user",
            "Feed User",
            "https://cdn.example.com/feed-user.jpg",
            3,
            1,
            2,
            true));
    }

    private static Mock<IUserResourceService> CreateUserResourceServiceMock(IReadOnlyDictionary<Guid, PublicUserProfileResult> profiles)
    {
        var userResourceService = new Mock<IUserResourceService>();
        userResourceService
            .Setup(item => item.GetPublicUserProfilesByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> userIds, CancellationToken _) =>
            {
                var response = userIds
                    .Where(profiles.ContainsKey)
                    .ToDictionary(id => id, id => profiles[id]);

                return Result.Success<IReadOnlyDictionary<Guid, PublicUserProfileResult>>(response);
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

    private static void SeedFollow(MyDbContext dbContext, Guid followId, Guid followerId, Guid followeeId, DateTime createdAt)
    {
        dbContext.Follows.Add(new Follow
        {
            Id = followId,
            FollowerId = followerId,
            FolloweeId = followeeId,
            CreatedAt = createdAt
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
