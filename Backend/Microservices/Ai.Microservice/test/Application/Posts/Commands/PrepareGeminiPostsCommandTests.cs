using Application.Abstractions.Resources;
using Application.Posts.Commands;
using Domain.Entities;
using Domain.Repositories;
using FluentAssertions;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace AiMicroservice.Tests.Application.Posts.Commands;

public sealed class PrepareGeminiPostsCommandTests
{
    [Fact]
    public async Task Handle_ShouldCreateOneEmptyDraftPostPerRequestedPlatform()
    {
        var resourceId = Guid.NewGuid();

        var postBuilderRepository = new Mock<IPostBuilderRepository>();
        var postRepository = new Mock<IPostRepository>();
        var workspaceRepository = new Mock<IWorkspaceRepository>();
        var userResourceService = new Mock<IUserResourceService>();

        userResourceService
            .Setup(service => service.GetPresignedResourcesAsync(
                It.IsAny<Guid>(),
                It.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { resourceId })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<UserResourcePresignResult>>(
            [
                new UserResourcePresignResult(
                    resourceId,
                    "https://cdn.example.com/resource.jpg",
                    "image/jpeg",
                    "image")
            ]));

        postRepository
            .Setup(repository => repository.AddAsync(It.IsAny<Post>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        postRepository
            .Setup(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        postBuilderRepository
            .Setup(repository => repository.AddAsync(It.IsAny<PostBuilder>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new PrepareGeminiPostsCommandHandler(
            postBuilderRepository.Object,
            postRepository.Object,
            workspaceRepository.Object,
            userResourceService.Object);

        var result = await handler.Handle(
            new PrepareGeminiPostsCommand(
                Guid.NewGuid(),
                null,
                [resourceId],
                [
                    new PrepareGeminiPostSocialMediaInput(
                        "facebook",
                        "posts",
                        [])
                ]),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PostBuilderId.Should().NotBeEmpty();
        result.Value.PostType.Should().Be("posts");
        result.Value.ResourceIds.Should().Equal(resourceId);
        result.Value.SocialMedia.Should().ContainSingle();
        result.Value.SocialMedia[0].SocialMediaId.Should().BeNull();
        result.Value.SocialMedia[0].Platform.Should().Be("facebook");
        result.Value.SocialMedia[0].Type.Should().Be("posts");
        result.Value.SocialMedia[0].Drafts.Should().HaveCount(1);
        result.Value.SocialMedia[0].Drafts[0].Caption.Should().BeEmpty();
        result.Value.SocialMedia[0].Drafts[0].Title.Should().BeNull();
        result.Value.SocialMedia[0].Drafts[0].Hashtags.Should().BeEmpty();
        result.Value.SocialMedia[0].Drafts[0].TrendingHashtags.Should().BeEmpty();
        result.Value.SocialMedia[0].Drafts[0].CallToAction.Should().BeNull();

        postRepository.Verify(repository => repository.AddAsync(
            It.Is<Post>(post =>
                post.PostBuilderId == result.Value.PostBuilderId &&
                post.SocialMediaId == null &&
                post.Platform == "facebook" &&
                post.Title == null &&
                post.Status == "draft" &&
                post.Content != null &&
                post.Content.Content == null &&
                post.Content.Hashtag == null &&
                post.Content.ResourceList != null &&
                post.Content.ResourceList.SequenceEqual(new[] { resourceId.ToString() }) &&
                post.Content.PostType == "posts"),
            It.IsAny<CancellationToken>()), Times.Once);

        postBuilderRepository.Verify(repository => repository.AddAsync(
            It.Is<PostBuilder>(builder =>
                builder.Id == result.Value.PostBuilderId &&
                builder.PostType == "posts" &&
                builder.ResourceIds == "[\"" + resourceId + "\"]"),
            It.IsAny<CancellationToken>()), Times.Once);

        postRepository.Verify(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
