using Application.Abstractions.Resources;
using Application.Abstractions.Configs;
using Application.Abstractions.Gemini;
using Application.Posts.Commands;
using Domain.Entities;
using Domain.Repositories;
using FluentAssertions;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace AiMicroservice.Tests.Application.Posts.Commands;

public sealed class GenerateSocialMediaCaptionsCommandTests
{
    [Fact]
    public async Task Handle_ShouldGenerateCaptionsForEachPlatformUsingConfiguredVarianceCount()
    {
        var userId = Guid.NewGuid();
        var firstPostId = Guid.NewGuid();
        var secondPostId = Guid.NewGuid();
        var firstResourceId = Guid.NewGuid();
        var secondResourceId = Guid.NewGuid();

        var postRepository = new Mock<IPostRepository>();
        var userResourceService = new Mock<IUserResourceService>();
        var userConfigService = new Mock<IUserConfigService>();
        var geminiCaptionService = new Mock<IGeminiCaptionService>();

        postRepository
            .Setup(repository => repository.GetByIdAsync(firstPostId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Post
            {
                Id = firstPostId,
                UserId = userId,
                Title = "Launch teaser",
                Content = new PostContent { Content = "Short product reveal" }
            });

        postRepository
            .Setup(repository => repository.GetByIdAsync(secondPostId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Post
            {
                Id = secondPostId,
                UserId = userId,
                Title = "Visual storytelling",
                Content = new PostContent { Content = "Polished studio shots" }
            });

        userResourceService
            .Setup(service => service.GetPresignedResourcesAsync(
                userId,
                It.Is<IReadOnlyList<Guid>>(ids =>
                    ids.Count == 2 &&
                    ids.Contains(firstResourceId) &&
                    ids.Contains(secondResourceId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<UserResourcePresignResult>>(
            [
                new UserResourcePresignResult(
                    firstResourceId,
                    "https://cdn.example.com/resource-1.jpg",
                    "image/jpeg",
                    "image"),
                new UserResourcePresignResult(
                    secondResourceId,
                    "https://cdn.example.com/resource-2.jpg",
                    "image/jpeg",
                    "image")
            ]));

        userConfigService
            .Setup(service => service.GetActiveConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<UserAiConfig?>(new UserAiConfig(
                Guid.NewGuid(),
                "gemini-test-model",
                null,
                4)));

        geminiCaptionService
            .Setup(service => service.GenerateSocialMediaCaptionsAsync(
                It.IsAny<GeminiSocialMediaCaptionRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((GeminiSocialMediaCaptionRequest request, CancellationToken _) =>
                Result.Success<IReadOnlyList<GeminiGeneratedCaption>>(
                [
                    new GeminiGeneratedCaption(
                        $"{request.Platform}-caption",
                        ["#launch", "#ai"],
                        ["#trend", "#fyp"],
                        "Try it now")
                ]));

        var handler = new GenerateSocialMediaCaptionsCommandHandler(
            postRepository.Object,
            userResourceService.Object,
            userConfigService.Object,
            geminiCaptionService.Object);

        var result = await handler.Handle(
            new GenerateSocialMediaCaptionsCommand(
                userId,
                [
                    new SocialMediaCaptionPostInput(firstPostId, "TikTok", [firstResourceId]),
                    new SocialMediaCaptionPostInput(secondPostId, "Instagram", [secondResourceId])
                ],
                "en",
                "Keep it energetic"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.SocialMedia.Select(item => item.SocialMediaType).Should().Equal("tiktok", "ig");
        result.Value.SocialMedia[0].PostId.Should().Be(firstPostId);
        result.Value.SocialMedia[0].ResourceList.Should().Equal(firstResourceId);
        result.Value.SocialMedia[0].Captions[0].Caption.Should().Be("tiktok-caption");
        result.Value.SocialMedia[1].Captions[0].Caption.Should().Be("ig-caption");

        geminiCaptionService.Verify(service => service.GenerateSocialMediaCaptionsAsync(
            It.Is<GeminiSocialMediaCaptionRequest>(request =>
                request.Resources.Count == 1 &&
                request.Resources[0].FileUri == "https://cdn.example.com/resource-1.jpg" &&
                request.InlineTemplateResource == null &&
                request.Platform == "tiktok" &&
                request.CaptionCount == 4 &&
                request.PreferredModel == "gemini-test-model"),
            It.IsAny<CancellationToken>()), Times.Once);

        geminiCaptionService.Verify(service => service.GenerateSocialMediaCaptionsAsync(
            It.Is<GeminiSocialMediaCaptionRequest>(request =>
                request.Resources.Count == 1 &&
                request.Resources[0].FileUri == "https://cdn.example.com/resource-2.jpg" &&
                request.InlineTemplateResource == null &&
                request.Platform == "ig" &&
                request.CaptionCount == 4 &&
                request.PreferredModel == "gemini-test-model"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldFailForUnsupportedPlatform()
    {
        var postRepository = new Mock<IPostRepository>();
        var userResourceService = new Mock<IUserResourceService>();
        var userConfigService = new Mock<IUserConfigService>();
        var geminiCaptionService = new Mock<IGeminiCaptionService>();

        var handler = new GenerateSocialMediaCaptionsCommandHandler(
            postRepository.Object,
            userResourceService.Object,
            userConfigService.Object,
            geminiCaptionService.Object);

        var result = await handler.Handle(
            new GenerateSocialMediaCaptionsCommand(
                Guid.NewGuid(),
                [
                    new SocialMediaCaptionPostInput(Guid.NewGuid(), "YouTube", [Guid.NewGuid()])
                ],
                "en",
                null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SocialMedia.UnsupportedPlatform");
        geminiCaptionService.VerifyNoOtherCalls();
    }
}
