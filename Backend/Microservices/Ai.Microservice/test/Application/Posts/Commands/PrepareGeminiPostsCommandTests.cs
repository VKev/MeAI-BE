using Application.Abstractions.Configs;
using Application.Abstractions.Gemini;
using Application.Abstractions.Resources;
using Application.Abstractions.SocialMedias;
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
    public async Task Handle_ShouldCreateDraftPostsForEachGeneratedCaption()
    {
        var socialMediaId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();

        var postBuilderRepository = new Mock<IPostBuilderRepository>();
        var postRepository = new Mock<IPostRepository>();
        var workspaceRepository = new Mock<IWorkspaceRepository>();
        var userConfigService = new Mock<IUserConfigService>();
        var userResourceService = new Mock<IUserResourceService>();
        var userSocialMediaService = new Mock<IUserSocialMediaService>();
        var geminiCaptionService = new Mock<IGeminiCaptionService>();

        userConfigService
            .Setup(service => service.GetActiveConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<UserAiConfig?>(new UserAiConfig(
                Guid.NewGuid(),
                "gemini-test-model",
                null,
                2)));

        userSocialMediaService
            .Setup(service => service.GetSocialMediasAsync(
                It.IsAny<Guid>(),
                It.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { socialMediaId })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<UserSocialMediaResult>>(
            [
                new UserSocialMediaResult(socialMediaId, "facebook", null)
            ]));

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

        geminiCaptionService
            .Setup(service => service.GenerateSocialMediaCaptionsAsync(
                It.Is<GeminiSocialMediaCaptionRequest>(request =>
                    request.InlineTemplateResource == null &&
                    request.Platform == "facebook" &&
                    request.Resources.Count == 1 &&
                    request.Resources[0].FileUri == "https://cdn.example.com/resource.jpg" &&
                    request.CaptionCount == 2),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<GeminiGeneratedCaption>>(
            [
                new GeminiGeneratedCaption(
                    "Caption one #Launch",
                    ["#Launch"],
                    ["#trend1"],
                    "Try now"),
                new GeminiGeneratedCaption(
                    "Caption two #Promo",
                    ["#Promo"],
                    ["#trend2"],
                    "Shop now")
            ]));

        geminiCaptionService
            .Setup(service => service.GenerateTitleAsync(
                It.IsAny<GeminiTitleRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((GeminiTitleRequest request, CancellationToken _) =>
                Result.Success($"Title for {request.Content.Split(' ')[0]}"));

        postRepository
            .Setup(repository => repository.AddAsync(It.IsAny<Post>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        postRepository
            .Setup(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        postBuilderRepository
            .Setup(repository => repository.AddAsync(It.IsAny<PostBuilder>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new PrepareGeminiPostsCommandHandler(
            postBuilderRepository.Object,
            postRepository.Object,
            workspaceRepository.Object,
            userConfigService.Object,
            userResourceService.Object,
            userSocialMediaService.Object,
            geminiCaptionService.Object);

        var result = await handler.Handle(
            new PrepareGeminiPostsCommand(
                Guid.NewGuid(),
                null,
                [resourceId],
                [
                    new PrepareGeminiPostSocialMediaInput(
                        socialMediaId,
                        null,
                        [])
                ],
                "posts",
                "English",
                "Keep it concise"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PostBuilderId.Should().NotBeEmpty();
        result.Value.ResourceIds.Should().Equal(resourceId);
        result.Value.SocialMedia.Should().ContainSingle();
        result.Value.SocialMedia[0].Type.Should().Be("facebook");
        result.Value.SocialMedia[0].Drafts.Should().HaveCount(2);
        result.Value.SocialMedia[0].Drafts[0].Caption.Should().Be("Caption one #Launch");
        result.Value.SocialMedia[0].Drafts[1].TrendingHashtags.Should().Equal("#trend2");

        postRepository.Verify(repository => repository.AddAsync(
            It.Is<Post>(post =>
                post.PostBuilderId == result.Value.PostBuilderId &&
                post.SocialMediaId == socialMediaId &&
                post.Platform == "facebook" &&
                post.Status == "draft" &&
                post.Content != null &&
                post.Content.ResourceList != null &&
                post.Content.ResourceList.SequenceEqual(new[] { resourceId.ToString() }) &&
                post.Content.PostType == "posts"),
            It.IsAny<CancellationToken>()), Times.Exactly(2));

        postBuilderRepository.Verify(repository => repository.AddAsync(
            It.Is<PostBuilder>(builder =>
                builder.Id == result.Value.PostBuilderId &&
                builder.PostType == "posts" &&
                builder.ResourceIds == "[\"" + resourceId + "\"]"),
            It.IsAny<CancellationToken>()), Times.Once);

        postRepository.Verify(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
