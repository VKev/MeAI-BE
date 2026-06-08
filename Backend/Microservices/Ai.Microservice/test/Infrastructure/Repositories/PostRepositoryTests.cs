using Domain.Entities;
using FluentAssertions;
using Infrastructure.Context;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace test;

public sealed class PostRepositoryTests
{
    [Fact]
    public async Task GetByUserIdAsync_ShouldExcludeUneditedPostBuilderDraftPlaceholders()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var dbContext = CreateDbContext();
        var repository = new PostRepository(dbContext);

        var placeholder = CreatePost(userId, now.AddMinutes(-1));
        placeholder.PostBuilderId = Guid.NewGuid();
        placeholder.Title = null;
        placeholder.SocialMediaId = null;
        placeholder.Status = "draft";
        placeholder.UpdatedAt = null;

        var editedDraft = CreatePost(userId, now.AddMinutes(-2));
        editedDraft.PostBuilderId = Guid.NewGuid();
        editedDraft.Title = null;
        editedDraft.SocialMediaId = null;
        editedDraft.Status = "draft";
        editedDraft.UpdatedAt = now;

        var normalDraft = CreatePost(userId, now.AddMinutes(-3));
        normalDraft.PostBuilderId = null;
        normalDraft.Title = null;
        normalDraft.Status = "draft";
        normalDraft.UpdatedAt = null;

        dbContext.Posts.AddRange(placeholder, editedDraft, normalDraft);
        await dbContext.SaveChangesAsync();

        var unfilteredPosts = await repository.GetByUserIdAsync(
            userId,
            cursorCreatedAt: null,
            cursorId: null,
            limit: 10,
            status: null,
            socialMediaId: null,
            platform: null,
            CancellationToken.None);

        unfilteredPosts.Select(post => post.Id).Should().BeEquivalentTo([editedDraft.Id, normalDraft.Id]);

        var draftPosts = await repository.GetByUserIdAsync(
            userId,
            cursorCreatedAt: null,
            cursorId: null,
            limit: 10,
            status: "draft",
            socialMediaId: null,
            platform: null,
            CancellationToken.None);

        draftPosts.Select(post => post.Id).Should().BeEquivalentTo([editedDraft.Id, normalDraft.Id]);
    }

    [Fact]
    public async Task WorkspaceMappings_ShouldKeepOneSocialPostVisibleInMultipleWorkspaces()
    {
        var userId = Guid.NewGuid();
        var socialMediaId = Guid.NewGuid();
        var firstWorkspaceId = Guid.NewGuid();
        var secondWorkspaceId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var dbContext = CreateDbContext();
        var repository = new PostRepository(dbContext);
        var post = CreatePost(userId, now);
        post.SocialMediaId = socialMediaId;
        post.Status = "published";

        dbContext.Posts.Add(post);
        dbContext.PostPublications.Add(new PostPublication
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PostId = post.Id,
            WorkspaceId = Guid.Empty,
            SocialMediaId = socialMediaId,
            SocialMediaType = "facebook",
            DestinationOwnerId = "page-1",
            ExternalContentId = "post-1",
            ExternalContentIdType = "post_id",
            ContentType = "posts",
            PublishStatus = "published",
            CreatedAt = now
        });
        await dbContext.SaveChangesAsync();

        (await repository.AttachSocialMediaPostsToWorkspaceAsync(
            userId,
            socialMediaId,
            firstWorkspaceId,
            now,
            CancellationToken.None)).Should().Be(1);
        (await repository.AttachSocialMediaPostsToWorkspaceAsync(
            userId,
            socialMediaId,
            secondWorkspaceId,
            now,
            CancellationToken.None)).Should().Be(1);

        var firstWorkspacePosts = await GetWorkspacePostsAsync(repository, userId, firstWorkspaceId);
        var secondWorkspacePosts = await GetWorkspacePostsAsync(repository, userId, secondWorkspaceId);
        firstWorkspacePosts.Select(item => item.Id).Should().ContainSingle().Which.Should().Be(post.Id);
        secondWorkspacePosts.Select(item => item.Id).Should().ContainSingle().Which.Should().Be(post.Id);

        (await repository.DetachSocialMediaPostsFromWorkspaceAsync(
            userId,
            socialMediaId,
            firstWorkspaceId,
            now.AddMinutes(1),
            CancellationToken.None)).Should().Be(1);

        firstWorkspacePosts = await GetWorkspacePostsAsync(repository, userId, firstWorkspaceId);
        secondWorkspacePosts = await GetWorkspacePostsAsync(repository, userId, secondWorkspaceId);
        firstWorkspacePosts.Should().BeEmpty();
        secondWorkspacePosts.Select(item => item.Id).Should().ContainSingle().Which.Should().Be(post.Id);
    }

    private static Task<IReadOnlyList<Post>> GetWorkspacePostsAsync(
        PostRepository repository,
        Guid userId,
        Guid workspaceId)
    {
        return repository.GetByUserIdAndWorkspaceIdAsync(
            userId,
            workspaceId,
            cursorCreatedAt: null,
            cursorId: null,
            limit: 10,
            status: null,
            socialMediaId: null,
            platform: null,
            CancellationToken.None);
    }

    private static MyDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new MyDbContext(options);
    }

    private static Post CreatePost(Guid userId, DateTime createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CreatedAt = createdAt,
            Platform = "facebook",
            Content = new PostContent
            {
                Content = "Caption",
                Hashtag = null,
                ResourceList = [],
                PostType = "posts"
            }
        };
}
