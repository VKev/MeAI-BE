using Application.Abstractions.Configs;
using Application.Abstractions.Gemini;
using Application.Abstractions.Resources;
using Application.Posts.Commands;
using Domain.Entities;
using Domain.Repositories;
using FluentAssertions;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace AiMicroservice.Tests.Application.Posts.Commands;

public sealed class CreateGeminiPostCommandTests
{
    [Fact]
    public async Task Handle_ShouldCreateBuilderAndDraftPost()
    {
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        PostBuilder? addedBuilder = null;
        Post? addedPost = null;

        var postRepository = new Mock<IPostRepository>(MockBehavior.Strict);
        postRepository
            .Setup(repository => repository.AddAsync(It.IsAny<Post>(), It.IsAny<CancellationToken>()))
            .Callback<Post, CancellationToken>((post, _) => addedPost = post)
            .Returns(Task.CompletedTask);
        postRepository
            .Setup(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var postBuilderRepository = new Mock<IPostBuilderRepository>(MockBehavior.Strict);
        postBuilderRepository
            .Setup(repository => repository.AddAsync(It.IsAny<PostBuilder>(), It.IsAny<CancellationToken>()))
            .Callback<PostBuilder, CancellationToken>((builder, _) => addedBuilder = builder)
            .Returns(Task.CompletedTask);

        var workspaceRepository = new Mock<IWorkspaceRepository>(MockBehavior.Strict);
        workspaceRepository
            .Setup(repository => repository.ExistsForUserAsync(workspaceId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var userConfigService = new Mock<IUserConfigService>(MockBehavior.Strict);
        userConfigService
            .Setup(service => service.GetActiveConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<UserAiConfig?>(null));

        var userResourceService = new Mock<IUserResourceService>(MockBehavior.Strict);
        userResourceService
            .Setup(service => service.GetPresignedResourcesAsync(
                userId,
                It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == resourceId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<UserResourcePresignResult>>(
            [
                new UserResourcePresignResult(
                    resourceId,
                    "https://cdn.example.com/resource.jpg",
                    "image/jpeg",
                    "image")
            ]));

        var geminiCaptionService = new Mock<IGeminiCaptionService>(MockBehavior.Strict);
        geminiCaptionService
            .Setup(service => service.GenerateCaptionAsync(
                It.Is<GeminiCaptionRequest>(request =>
                    request.PostType == "posts" &&
                    request.Resources.Count == 1),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success("Generated caption #tag"));
        geminiCaptionService
            .Setup(service => service.GenerateTitleAsync(
                It.Is<GeminiTitleRequest>(request => request.Content == "Generated caption #tag"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success("Generated title"));

        var handler = new CreateGeminiPostCommandHandler(
            postRepository.Object,
            postBuilderRepository.Object,
            workspaceRepository.Object,
            userConfigService.Object,
            userResourceService.Object,
            geminiCaptionService.Object);

        var result = await handler.Handle(
            new CreateGeminiPostCommand(
                userId,
                workspaceId,
                [resourceId],
                null,
                "posts",
                null,
                null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PostBuilderId.Should().NotBeEmpty();
        result.Value.PostId.Should().NotBeEmpty();
        result.Value.CaptionGenerated.Should().BeTrue();
        result.Value.ResourceIds.Should().Equal(resourceId);

        addedBuilder.Should().NotBeNull();
        addedBuilder!.Id.Should().Be(result.Value.PostBuilderId);
        addedBuilder.OriginKind.Should().Be(PostBuilderOriginKinds.AiGeminiDraft);
        addedBuilder.WorkspaceId.Should().Be(workspaceId);
        addedBuilder.ResourceIds.Should().Be($"[\"{resourceId}\"]");

        addedPost.Should().NotBeNull();
        addedPost!.PostBuilderId.Should().Be(result.Value.PostBuilderId);
        addedPost.WorkspaceId.Should().Be(workspaceId);
        addedPost.Title.Should().Be("Generated title");
        addedPost.Content!.Content.Should().Be("Generated caption #tag");

        postRepository.VerifyAll();
        postBuilderRepository.VerifyAll();
        workspaceRepository.VerifyAll();
        userConfigService.VerifyAll();
        userResourceService.VerifyAll();
        geminiCaptionService.VerifyAll();
    }
}
