using Application.Posts.Commands;
using Domain.Entities;
using Domain.Repositories;
using FluentAssertions;
using MassTransit;
using Moq;
using SharedLibrary.Contracts.Publishing;

namespace AiMicroservice.Tests.Application.Posts.Commands;

public sealed class UnpublishPostCommandTests
{
    [Fact]
    public async Task Handle_ShouldRejectInstagramBeforeMutatingStateOrPublishingMessage()
    {
        var userId = Guid.NewGuid();
        var postId = Guid.NewGuid();
        var post = new Post
        {
            Id = postId,
            UserId = userId,
            Status = "published"
        };
        var publication = new PostPublication
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PostId = postId,
            WorkspaceId = Guid.NewGuid(),
            SocialMediaId = Guid.NewGuid(),
            SocialMediaType = "instagram",
            DestinationOwnerId = "instagram-user-id",
            ExternalContentId = "instagram-media-id",
            ExternalContentIdType = "media_id",
            ContentType = "posts",
            PublishStatus = "published"
        };

        var postRepository = new Mock<IPostRepository>();
        postRepository
            .Setup(repository => repository.GetByIdForUpdateAsync(postId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(post);

        var postPublicationRepository = new Mock<IPostPublicationRepository>();
        postPublicationRepository
            .Setup(repository => repository.GetByPostIdForUpdateAsync(postId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([publication]);

        var bus = new Mock<IBus>();
        var handler = new UnpublishPostCommandHandler(
            postRepository.Object,
            postPublicationRepository.Object,
            bus.Object);

        var result = await handler.Handle(
            new UnpublishPostCommand(userId, postId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Instagram.DeleteNotSupported");
        post.Status.Should().Be("published");
        publication.PublishStatus.Should().Be("published");
        postRepository.Verify(repository => repository.Update(It.IsAny<Post>()), Times.Never);
        postRepository.Verify(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        postPublicationRepository.Verify(repository => repository.Update(It.IsAny<PostPublication>()), Times.Never);
        bus.Verify(
            instance => instance.Publish(It.IsAny<UnpublishFromTargetRequested>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
