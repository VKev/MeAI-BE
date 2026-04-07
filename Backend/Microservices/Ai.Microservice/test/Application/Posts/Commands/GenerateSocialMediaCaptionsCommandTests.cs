using Application.Abstractions.Configs;
using Application.Abstractions.Gemini;
using Application.Posts.Commands;
using FluentAssertions;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace AiMicroservice.Tests.Application.Posts.Commands;

public sealed class GenerateSocialMediaCaptionsCommandTests
{
    [Fact]
    public async Task Handle_ShouldGenerateCaptionsForEachPlatformUsingConfiguredVarianceCount()
    {
        var userConfigService = new Mock<IUserConfigService>();
        var geminiCaptionService = new Mock<IGeminiCaptionService>();

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
            userConfigService.Object,
            geminiCaptionService.Object);

        var result = await handler.Handle(
            new GenerateSocialMediaCaptionsCommand(
                Guid.NewGuid(),
                new GeminiTemplateResourceInput("template.png", "image/png", [1, 2, 3]),
                [
                    new SocialMediaCaptionPlatformInput("TikTok", [" teaser ", "launch", "launch"]),
                    new SocialMediaCaptionPlatformInput("Instagram", ["visual storytelling"])
                ],
                "en",
                "Keep it energetic"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TemplateFileName.Should().Be("template.png");
        result.Value.TemplateMimeType.Should().Be("image/png");
        result.Value.SocialMedia.Select(item => item.Type).Should().Equal("tiktok", "ig");
        result.Value.SocialMedia[0].ResourceList.Should().Equal("teaser", "launch");
        result.Value.SocialMedia[0].Captions[0].Caption.Should().Be("tiktok-caption");
        result.Value.SocialMedia[1].Captions[0].Caption.Should().Be("ig-caption");

        geminiCaptionService.Verify(service => service.GenerateSocialMediaCaptionsAsync(
            It.Is<GeminiSocialMediaCaptionRequest>(request =>
                request.Resources.Count == 0 &&
                request.InlineTemplateResource != null &&
                request.Platform == "tiktok" &&
                request.CaptionCount == 4 &&
                request.PreferredModel == "gemini-test-model"),
            It.IsAny<CancellationToken>()), Times.Once);

        geminiCaptionService.Verify(service => service.GenerateSocialMediaCaptionsAsync(
            It.Is<GeminiSocialMediaCaptionRequest>(request =>
                request.Resources.Count == 0 &&
                request.InlineTemplateResource != null &&
                request.Platform == "ig" &&
                request.CaptionCount == 4 &&
                request.PreferredModel == "gemini-test-model"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldFailForUnsupportedPlatform()
    {
        var userConfigService = new Mock<IUserConfigService>();
        var geminiCaptionService = new Mock<IGeminiCaptionService>();

        var handler = new GenerateSocialMediaCaptionsCommandHandler(
            userConfigService.Object,
            geminiCaptionService.Object);

        var result = await handler.Handle(
            new GenerateSocialMediaCaptionsCommand(
                Guid.NewGuid(),
                new GeminiTemplateResourceInput("template.png", "image/png", [1, 2, 3]),
                [
                    new SocialMediaCaptionPlatformInput("YouTube", ["video"])
                ],
                "en",
                null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SocialMedia.UnsupportedPlatform");
        geminiCaptionService.VerifyNoOtherCalls();
    }
}
