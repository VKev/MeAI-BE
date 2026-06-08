using Domain.Entities;
using FluentAssertions;
using Infrastructure.Context;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace test;

public sealed class PostPublicationRepositoryTests
{
    [Fact]
    public async Task ExternalContentLookup_ShouldKeepIdenticalSocialPostsIsolatedByUser()
    {
        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var dbContext = CreateDbContext();
        var repository = new PostPublicationRepository(dbContext);

        var firstPublication = CreatePublication(firstUserId, Guid.NewGuid(), now);
        var secondPublication = CreatePublication(secondUserId, Guid.NewGuid(), now);
        dbContext.PostPublications.AddRange(firstPublication, secondPublication);
        await dbContext.SaveChangesAsync();

        var firstResult = await repository.GetByExternalContentKeyForUpdateAsync(
            firstUserId,
            "facebook",
            "shared-page",
            "shared-post",
            CancellationToken.None);
        var secondResult = await repository.GetByExternalContentKeyForUpdateAsync(
            secondUserId,
            "facebook",
            "shared-page",
            "shared-post",
            CancellationToken.None);

        firstResult.Should().NotBeNull();
        firstResult!.Id.Should().Be(firstPublication.Id);
        secondResult.Should().NotBeNull();
        secondResult!.Id.Should().Be(secondPublication.Id);
    }

    private static MyDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new MyDbContext(options);
    }

    private static PostPublication CreatePublication(Guid userId, Guid socialMediaId, DateTime createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PostId = Guid.NewGuid(),
            WorkspaceId = Guid.Empty,
            SocialMediaId = socialMediaId,
            SocialMediaType = "facebook",
            DestinationOwnerId = "shared-page",
            ExternalContentId = "shared-post",
            ExternalContentIdType = "post_id",
            ContentType = "posts",
            PublishStatus = "published",
            CreatedAt = createdAt
        };
}
