using Application.Abstractions.Resources;
using Application.Posts;
using Application.Posts.Models;
using Application.Posts.Queries;
using Domain.Entities;
using Domain.Repositories;
using FluentAssertions;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace test;

public sealed class PostPagingQueryTests
{
    [Fact]
    public async Task GetUserPosts_ShouldPassCursorArgumentsToRepository_AndReturnPagedResults()
    {
        var userId = Guid.NewGuid();
        var cursorCreatedAt = new DateTime(2026, 03, 20, 10, 00, 00, DateTimeKind.Utc);
        var cursorId = Guid.NewGuid();
        var post = CreatePost(userId, cursorCreatedAt.AddMinutes(-5));

        var postRepository = new Mock<IPostRepository>();
        postRepository
            .Setup(repository => repository.GetByUserIdAsync(
                userId,
                cursorCreatedAt,
                cursorId,
                25,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Post> { post });

        var handler = new GetUserPostsQueryHandler(postRepository.Object, CreatePostResponseBuilder());

        var result = await handler.Handle(
            new GetUserPostsQuery(userId, cursorCreatedAt, cursorId, 25),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value.Single().Id.Should().Be(post.Id);
        postRepository.VerifyAll();
    }

    [Fact]
    public async Task GetWorkspacePosts_ShouldClampLimit_AndReturnWorkspacePosts()
    {
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var post = CreatePost(userId, new DateTime(2026, 03, 20, 9, 00, 00, DateTimeKind.Utc), workspaceId);

        var postRepository = new Mock<IPostRepository>();
        postRepository
            .Setup(repository => repository.GetByUserIdAndWorkspaceIdAsync(
                userId,
                workspaceId,
                null,
                null,
                100,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Post> { post });

        var workspaceRepository = new Mock<IWorkspaceRepository>();
        workspaceRepository
            .Setup(repository => repository.ExistsForUserAsync(workspaceId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = new GetWorkspacePostsQueryHandler(
            postRepository.Object,
            workspaceRepository.Object,
            CreatePostResponseBuilder());

        var result = await handler.Handle(
            new GetWorkspacePostsQuery(workspaceId, userId, null, null, 500),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value.Single().WorkspaceId.Should().Be(workspaceId);
        workspaceRepository.VerifyAll();
        postRepository.VerifyAll();
    }

    private static PostResponseBuilder CreatePostResponseBuilder()
    {
        var userResourceService = new Mock<IUserResourceService>(MockBehavior.Strict);
        var publicationRepository = new Mock<IPostPublicationRepository>();
        publicationRepository
            .Setup(repository => repository.GetByPostIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PostPublication>());

        return new PostResponseBuilder(userResourceService.Object, publicationRepository.Object);
    }

    private static Post CreatePost(Guid userId, DateTime createdAt, Guid? workspaceId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            WorkspaceId = workspaceId,
            Title = "Post",
            Content = new PostContent
            {
                Content = "Body",
                Hashtag = "#tag",
                PostType = "text",
                ResourceList = new List<string>()
            },
            Status = "draft",
            CreatedAt = createdAt
        };
}
