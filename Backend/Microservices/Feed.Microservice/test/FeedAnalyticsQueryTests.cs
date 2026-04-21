using Application.Analytics.Queries;
using Application.Abstractions.Resources;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Context;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace test;

public sealed class FeedAnalyticsQueryTests
{
    [Fact]
    public async Task GetFeedDashboardSummary_ShouldAggregateVisiblePostsAndProfileStats()
    {
        await using var dbContext = CreateDbContext();

        var targetUserId = Guid.NewGuid();
        var followerOneId = Guid.NewGuid();
        var followerTwoId = Guid.NewGuid();
        var followingId = Guid.NewGuid();
        var newestPostId = Guid.NewGuid();
        var secondPostId = Guid.NewGuid();
        var hiddenPostId = Guid.NewGuid();
        var newestResourceId = Guid.NewGuid();
        var secondResourceId = Guid.NewGuid();

        dbContext.Posts.AddRange(
            CreatePost(newestPostId, targetUserId, "Newest #alpha #beta", new DateTime(2026, 4, 20, 12, 0, 0, DateTimeKind.Utc), 5, new[] { newestResourceId }),
            CreatePost(secondPostId, targetUserId, "Second #beta", new DateTime(2026, 4, 19, 12, 0, 0, DateTimeKind.Utc), 3, new[] { secondResourceId }),
            CreatePost(hiddenPostId, targetUserId, "Older #archive", new DateTime(2026, 4, 18, 12, 0, 0, DateTimeKind.Utc), 9, Array.Empty<Guid>()));

        dbContext.Comments.AddRange(
            CreateComment(Guid.NewGuid(), newestPostId, followerOneId, null, 2, new DateTime(2026, 4, 20, 12, 5, 0, DateTimeKind.Utc)),
            CreateComment(Guid.NewGuid(), newestPostId, followerTwoId, Guid.NewGuid(), 0, new DateTime(2026, 4, 20, 12, 6, 0, DateTimeKind.Utc)),
            CreateComment(Guid.NewGuid(), secondPostId, followerOneId, null, 1, new DateTime(2026, 4, 19, 12, 5, 0, DateTimeKind.Utc)),
            CreateComment(Guid.NewGuid(), hiddenPostId, followerTwoId, null, 0, new DateTime(2026, 4, 18, 12, 5, 0, DateTimeKind.Utc)));

        dbContext.Follows.AddRange(
            CreateFollow(Guid.NewGuid(), followerOneId, targetUserId),
            CreateFollow(Guid.NewGuid(), followerTwoId, targetUserId),
            CreateFollow(Guid.NewGuid(), targetUserId, followingId));

        var alphaId = Guid.NewGuid();
        var betaId = Guid.NewGuid();
        var archiveId = Guid.NewGuid();

        dbContext.Hashtags.AddRange(
            new Hashtag { Id = alphaId, Name = "#alpha", CreatedAt = DateTime.UtcNow },
            new Hashtag { Id = betaId, Name = "#beta", CreatedAt = DateTime.UtcNow },
            new Hashtag { Id = archiveId, Name = "#archive", CreatedAt = DateTime.UtcNow });

        dbContext.PostHashtags.AddRange(
            new PostHashtag { Id = Guid.NewGuid(), PostId = newestPostId, HashtagId = alphaId, CreatedAt = DateTime.UtcNow },
            new PostHashtag { Id = Guid.NewGuid(), PostId = newestPostId, HashtagId = betaId, CreatedAt = DateTime.UtcNow },
            new PostHashtag { Id = Guid.NewGuid(), PostId = secondPostId, HashtagId = betaId, CreatedAt = DateTime.UtcNow },
            new PostHashtag { Id = Guid.NewGuid(), PostId = hiddenPostId, HashtagId = archiveId, CreatedAt = DateTime.UtcNow });

        await dbContext.SaveChangesAsync();

        using var unitOfWork = new UnitOfWork(dbContext);
        var userResourceService = new Mock<IUserResourceService>();

        userResourceService
            .Setup(service => service.GetPublicUserProfileByUsernameAsync("feed-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PublicUserProfileResult(
                targetUserId,
                "feed-user",
                "Feed User",
                "https://cdn.example.com/avatar.jpg")));

        userResourceService
            .Setup(service => service.GetPublicPresignedResourcesAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> resourceIds, CancellationToken _) =>
            {
                var resources = resourceIds
                    .Distinct()
                    .Select(resourceId => new UserResourcePresignResult(
                        resourceId,
                        $"https://cdn.example.com/{resourceId}.jpg",
                        "image/jpeg",
                        "image"))
                    .ToList();

                return Result.Success<IReadOnlyList<UserResourcePresignResult>>(resources);
            });

        var handler = new GetFeedDashboardSummaryQueryHandler(unitOfWork, userResourceService.Object);

        var result = await handler.Handle(
            new GetFeedDashboardSummaryQuery("feed-user", null, 2),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.FetchedPostCount.Should().Be(2);
        result.Value.HasMorePosts.Should().BeTrue();
        result.Value.LatestPublishedPostId.Should().Be(newestPostId);
        result.Value.Profile.FollowersCount.Should().Be(2);
        result.Value.Profile.FollowingCount.Should().Be(1);
        result.Value.Profile.MediaCount.Should().Be(3);
        result.Value.AggregatedStats.Should().BeEquivalentTo(new
        {
            Likes = 8L,
            TopLevelComments = 2L,
            Replies = 1L,
            TotalDiscussion = 3L,
            TotalInteractions = 11L,
            MediaCount = 2L,
            HashtagCount = 3L
        });
        result.Value.Posts.Should().HaveCount(2);
        result.Value.Posts[0].PostId.Should().Be(newestPostId);
        result.Value.Posts[0].MediaUrl.Should().Be($"https://cdn.example.com/{newestResourceId}.jpg");
        result.Value.Posts[0].Stats.Should().BeEquivalentTo(new
        {
            Likes = 5L,
            TopLevelComments = 1L,
            Replies = 1L,
            TotalDiscussion = 2L,
            TotalInteractions = 7L,
            MediaCount = 1L,
            HashtagCount = 2L
        });
        result.Value.Posts[0].Hashtags.Should().Equal("#alpha", "#beta");
    }

    [Fact]
    public async Task GetFeedPostAnalytics_ShouldReturnDiscussionBreakdownAndCommentSamples()
    {
        await using var dbContext = CreateDbContext();

        var authorId = Guid.NewGuid();
        var commentAuthorId = Guid.NewGuid();
        var anotherCommentAuthorId = Guid.NewGuid();
        var postId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var topCommentId = Guid.NewGuid();
        var secondTopCommentId = Guid.NewGuid();

        dbContext.Posts.Add(CreatePost(
            postId,
            authorId,
            "Analytics #feed #insight",
            new DateTime(2026, 4, 21, 9, 0, 0, DateTimeKind.Utc),
            12,
            new[] { resourceId }));

        dbContext.Comments.AddRange(
            CreateComment(topCommentId, postId, commentAuthorId, null, 4, new DateTime(2026, 4, 21, 9, 5, 0, DateTimeKind.Utc), repliesCount: 2),
            CreateComment(Guid.NewGuid(), postId, anotherCommentAuthorId, topCommentId, 1, new DateTime(2026, 4, 21, 9, 6, 0, DateTimeKind.Utc)),
            CreateComment(secondTopCommentId, postId, anotherCommentAuthorId, null, 0, new DateTime(2026, 4, 21, 9, 7, 0, DateTimeKind.Utc), repliesCount: 0));

        var feedHashtagId = Guid.NewGuid();
        var insightHashtagId = Guid.NewGuid();
        dbContext.Hashtags.AddRange(
            new Hashtag { Id = feedHashtagId, Name = "#feed", CreatedAt = DateTime.UtcNow },
            new Hashtag { Id = insightHashtagId, Name = "#insight", CreatedAt = DateTime.UtcNow });
        dbContext.PostHashtags.AddRange(
            new PostHashtag { Id = Guid.NewGuid(), PostId = postId, HashtagId = feedHashtagId, CreatedAt = DateTime.UtcNow },
            new PostHashtag { Id = Guid.NewGuid(), PostId = postId, HashtagId = insightHashtagId, CreatedAt = DateTime.UtcNow });
        dbContext.Follows.Add(CreateFollow(Guid.NewGuid(), commentAuthorId, authorId));

        await dbContext.SaveChangesAsync();

        using var unitOfWork = new UnitOfWork(dbContext);
        var userResourceService = new Mock<IUserResourceService>();

        userResourceService
            .Setup(service => service.GetPublicPresignedResourcesAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<UserResourcePresignResult>>(
                new[]
                {
                    new UserResourcePresignResult(resourceId, "https://cdn.example.com/post.jpg", "image/jpeg", "image")
                }));

        userResourceService
            .Setup(service => service.GetPublicUserProfilesByIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> userIds, CancellationToken _) =>
            {
                var profiles = new Dictionary<Guid, PublicUserProfileResult>();
                foreach (var userId in userIds.Distinct())
                {
                    profiles[userId] = userId switch
                    {
                        _ when userId == authorId => new PublicUserProfileResult(authorId, "author-user", "Author User", "https://cdn.example.com/author.jpg"),
                        _ when userId == commentAuthorId => new PublicUserProfileResult(commentAuthorId, "commenter", "Commenter", "https://cdn.example.com/commenter.jpg"),
                        _ when userId == anotherCommentAuthorId => new PublicUserProfileResult(anotherCommentAuthorId, "second-commenter", "Second Commenter", null),
                        _ => new PublicUserProfileResult(userId, userId.ToString(), null, null)
                    };
                }

                return Result.Success<IReadOnlyDictionary<Guid, PublicUserProfileResult>>(profiles);
            });

        var handler = new GetFeedPostAnalyticsQueryHandler(unitOfWork, userResourceService.Object);

        var result = await handler.Handle(
            new GetFeedPostAnalyticsQuery(postId, null, 1),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Profile.Username.Should().Be("author-user");
        result.Value.Profile.FollowersCount.Should().Be(1);
        result.Value.Post.PostId.Should().Be(postId);
        result.Value.Post.MediaUrl.Should().Be("https://cdn.example.com/post.jpg");
        result.Value.Post.Stats.Should().BeEquivalentTo(new
        {
            Likes = 12L,
            TopLevelComments = 2L,
            Replies = 1L,
            TotalDiscussion = 3L,
            TotalInteractions = 15L,
            MediaCount = 1L,
            HashtagCount = 2L
        });
        result.Value.CommentSamples.Should().HaveCount(1);
        result.Value.CommentSamples[0].CommentId.Should().Be(secondTopCommentId);
        result.Value.CommentSamples[0].Username.Should().Be("second-commenter");
        result.Value.CommentSamples[0].RepliesCount.Should().Be(0);
    }

    private static MyDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new MyDbContext(options);
    }

    private static Post CreatePost(
        Guid postId,
        Guid userId,
        string content,
        DateTime createdAt,
        int likesCount,
        Guid[] resourceIds)
    {
        return new Post
        {
            Id = postId,
            UserId = userId,
            Content = content,
            ResourceIds = resourceIds,
            LikesCount = likesCount,
            CommentsCount = 0,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            DeletedAt = null,
            IsDeleted = false
        };
    }

    private static Comment CreateComment(
        Guid commentId,
        Guid postId,
        Guid userId,
        Guid? parentCommentId,
        int likesCount,
        DateTime createdAt,
        int repliesCount = 0)
    {
        return new Comment
        {
            Id = commentId,
            PostId = postId,
            UserId = userId,
            ParentCommentId = parentCommentId,
            Content = "comment",
            LikesCount = likesCount,
            RepliesCount = repliesCount,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            DeletedAt = null,
            IsDeleted = false
        };
    }

    private static Follow CreateFollow(Guid followId, Guid followerId, Guid followeeId)
    {
        return new Follow
        {
            Id = followId,
            FollowerId = followerId,
            FolloweeId = followeeId,
            CreatedAt = DateTime.UtcNow
        };
    }
}
