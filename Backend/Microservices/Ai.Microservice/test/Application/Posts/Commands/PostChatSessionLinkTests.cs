using Application.Abstractions.Resources;
using Application.Posts;
using Application.Posts.Commands;
using Domain.Entities;
using Domain.Repositories;
using FluentAssertions;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace AiMicroservice.Tests.Application.Posts.Commands;

public sealed class PostChatSessionLinkTests
{
    [Fact]
    public async Task CreatePost_ShouldLinkPostToOwnedChatSession()
    {
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var chatSessionId = Guid.NewGuid();
        Post? addedPost = null;

        var postRepository = new Mock<IPostRepository>(MockBehavior.Strict);
        postRepository
            .Setup(repository => repository.AddAsync(It.IsAny<Post>(), It.IsAny<CancellationToken>()))
            .Callback<Post, CancellationToken>((post, _) => addedPost = post)
            .Returns(Task.CompletedTask);
        postRepository
            .Setup(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var workspaceRepository = new Mock<IWorkspaceRepository>(MockBehavior.Strict);
        workspaceRepository
            .Setup(repository => repository.ExistsForUserAsync(workspaceId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var chatSessionRepository = new Mock<IChatSessionRepository>(MockBehavior.Strict);
        chatSessionRepository
            .Setup(repository => repository.GetByIdAsync(chatSessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatSession
            {
                Id = chatSessionId,
                UserId = userId,
                WorkspaceId = workspaceId
            });

        var handler = new CreatePostCommandHandler(
            postRepository.Object,
            workspaceRepository.Object,
            chatSessionRepository.Object,
            CreatePostResponseBuilder());

        var result = await handler.Handle(
            new CreatePostCommand(
                userId,
                null,
                chatSessionId,
                null,
                "Draft",
                new PostContent { Content = "Body", PostType = "posts", ResourceList = [] },
                "draft"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ChatSessionId.Should().Be(chatSessionId);
        result.Value.WorkspaceId.Should().Be(workspaceId);
        addedPost.Should().NotBeNull();
        addedPost!.ChatSessionId.Should().Be(chatSessionId);
        addedPost.WorkspaceId.Should().Be(workspaceId);

        postRepository.VerifyAll();
        workspaceRepository.VerifyAll();
        chatSessionRepository.VerifyAll();
    }

    private static PostResponseBuilder CreatePostResponseBuilder()
    {
        var userResourceService = new Mock<IUserResourceService>(MockBehavior.Strict);
        userResourceService
            .Setup(service => service.GetPublicUserProfilesByIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> userIds, CancellationToken _) =>
                Result.Success<IReadOnlyDictionary<Guid, PublicUserProfileResult>>(
                    userIds.ToDictionary(
                        id => id,
                        id => new PublicUserProfileResult(id, $"user-{id:N}", null, null))));

        var publicationRepository = new Mock<IPostPublicationRepository>(MockBehavior.Strict);
        publicationRepository
            .Setup(repository => repository.GetByPostIdsAsync(
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PostPublication>());

        return new PostResponseBuilder(userResourceService.Object, publicationRepository.Object);
    }
}
