using System.Net;
using Application.Abstractions.Publishing;
using Application.Abstractions.Resources;
using FluentAssertions;
using Infrastructure.Logic.Publishing;
using Microsoft.Extensions.Logging;
using Moq;
using SharedLibrary.Common.ResponseModel;
using SkiaSharp;

namespace AiMicroservice.Tests.Infrastructure.Logic.Publishing;

public sealed class SocialPublishMediaNormalizerTests
{
    [Fact]
    public async Task NormalizeAsync_ShouldCreateBoundedJpegDerivative_ForTikTokPhotoPost()
    {
        var userId = Guid.CreateVersion7();
        var sourceResourceId = Guid.CreateVersion7();
        var derivativeResourceId = Guid.CreateVersion7();
        var pngBytes = await CreatePngAsync(1200, 2000);
        string? capturedDataUrl = null;

        var userResourceService = new Mock<IUserResourceService>();
        userResourceService
            .Setup(service => service.CreateResourcesFromUrlsAsync(
                userId,
                It.IsAny<IReadOnlyList<string>>(),
                "social_publish_derivative",
                "image",
                It.IsAny<CancellationToken>(),
                null,
                It.IsAny<SharedLibrary.Common.Resources.ResourceProvenanceMetadata?>()))
            .Callback<Guid, IReadOnlyList<string>, string?, string?, CancellationToken, Guid?, SharedLibrary.Common.Resources.ResourceProvenanceMetadata?>(
                (_, urls, _, _, _, _, _) => capturedDataUrl = urls.Single())
            .ReturnsAsync(Result.Success<IReadOnlyList<UserResourceCreatedResult>>(
                new[]
                {
                    new UserResourceCreatedResult(
                        derivativeResourceId,
                        "https://cdn.example.com/derivative.jpg",
                        "image/jpeg",
                        "image")
                }));

        var service = CreateService(pngBytes, "image/png", userResourceService.Object);
        var result = await service.NormalizeAsync(
            userId,
            workspaceId: null,
            platform: "tiktok",
            postType: "posts",
            resources: new[]
            {
                new UserResourcePresignResult(
                    sourceResourceId,
                    "https://cdn.example.com/original.png",
                    "image/png",
                    "image")
            },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TemporaryResourceIds.Should().ContainSingle().Which.Should().Be(derivativeResourceId);
        result.Value.Resources.Should().ContainSingle();
        result.Value.Resources[0].ResourceId.Should().Be(derivativeResourceId);
        capturedDataUrl.Should().StartWith("data:image/jpeg;base64,");

        var jpegBytes = Convert.FromBase64String(capturedDataUrl!["data:image/jpeg;base64,".Length..]);
        using var converted = SKBitmap.Decode(jpegBytes);
        converted.Should().NotBeNull();
        converted.Width.Should().BeLessThanOrEqualTo(1080);
        converted.Height.Should().BeLessThanOrEqualTo(1920);
    }

    [Fact]
    public async Task NormalizeAsync_ShouldKeepSupportedInstagramPng_WithoutCreatingDerivative()
    {
        var userId = Guid.CreateVersion7();
        var resourceId = Guid.CreateVersion7();
        var resource = new UserResourcePresignResult(
            resourceId,
            "https://cdn.example.com/original.png",
            "image/png",
            "image");
        var userResourceService = new Mock<IUserResourceService>(MockBehavior.Strict);
        var service = CreateService(Array.Empty<byte>(), "image/png", userResourceService.Object);

        var result = await service.NormalizeAsync(
            userId,
            workspaceId: null,
            platform: "instagram",
            postType: "posts",
            resources: new[] { resource },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Resources.Should().ContainSingle().Which.Should().Be(resource);
        result.Value.TemporaryResourceIds.Should().BeEmpty();
    }

    [Fact]
    public async Task NormalizeAsync_ShouldCreateJpegDerivative_ForUnsupportedInstagramWebp()
    {
        var userId = Guid.CreateVersion7();
        var sourceResourceId = Guid.CreateVersion7();
        var derivativeResourceId = Guid.CreateVersion7();
        var webpBytes = await CreateWebpAsync(640, 480);
        string? capturedDataUrl = null;

        var userResourceService = new Mock<IUserResourceService>();
        userResourceService
            .Setup(service => service.CreateResourcesFromUrlsAsync(
                userId,
                It.IsAny<IReadOnlyList<string>>(),
                "social_publish_derivative",
                "image",
                It.IsAny<CancellationToken>(),
                null,
                It.IsAny<SharedLibrary.Common.Resources.ResourceProvenanceMetadata?>()))
            .Callback<Guid, IReadOnlyList<string>, string?, string?, CancellationToken, Guid?, SharedLibrary.Common.Resources.ResourceProvenanceMetadata?>(
                (_, urls, _, _, _, _, _) => capturedDataUrl = urls.Single())
            .ReturnsAsync(Result.Success<IReadOnlyList<UserResourceCreatedResult>>(
                new[]
                {
                    new UserResourceCreatedResult(
                        derivativeResourceId,
                        "https://cdn.example.com/derivative.jpg",
                        "image/jpeg",
                        "image")
                }));

        var service = CreateService(webpBytes, "image/webp", userResourceService.Object);
        var result = await service.NormalizeAsync(
            userId,
            workspaceId: null,
            platform: "instagram",
            postType: "posts",
            resources: new[]
            {
                new UserResourcePresignResult(
                    sourceResourceId,
                    "https://cdn.example.com/original.webp",
                    "image/webp",
                    "image")
            },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TemporaryResourceIds.Should().ContainSingle().Which.Should().Be(derivativeResourceId);
        result.Value.Resources.Should().ContainSingle();
        result.Value.Resources[0].ContentType.Should().Be("image/jpeg");
        capturedDataUrl.Should().StartWith("data:image/jpeg;base64,");
    }

    [Fact]
    public async Task NormalizeAsync_ShouldCreateMp4Derivative_ForWebmVideo()
    {
        var userId = Guid.CreateVersion7();
        var sourceResourceId = Guid.CreateVersion7();
        var derivativeResourceId = Guid.CreateVersion7();
        var sourceResource = new UserResourcePresignResult(
            sourceResourceId,
            "https://cdn.example.com/original.webm",
            "video/webm",
            "video");
        var mp4Bytes = new byte[] { 1, 2, 3, 4 };
        string? capturedDataUrl = null;

        var userResourceService = new Mock<IUserResourceService>();
        userResourceService
            .Setup(service => service.CreateResourcesFromUrlsAsync(
                userId,
                It.IsAny<IReadOnlyList<string>>(),
                "social_publish_derivative",
                "video",
                It.IsAny<CancellationToken>(),
                null,
                It.IsAny<SharedLibrary.Common.Resources.ResourceProvenanceMetadata?>()))
            .Callback<Guid, IReadOnlyList<string>, string?, string?, CancellationToken, Guid?, SharedLibrary.Common.Resources.ResourceProvenanceMetadata?>(
                (_, urls, _, _, _, _, _) => capturedDataUrl = urls.Single())
            .ReturnsAsync(Result.Success<IReadOnlyList<UserResourceCreatedResult>>(
                new[]
                {
                    new UserResourceCreatedResult(
                        derivativeResourceId,
                        "https://cdn.example.com/derivative.mp4",
                        "video/mp4",
                        "video")
                }));
        var videoTranscoder = new Mock<ISocialPublishVideoTranscoder>();
        videoTranscoder
            .Setup(service => service.ConvertToMp4Async(sourceResource, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(mp4Bytes));

        var service = CreateService(
            Array.Empty<byte>(),
            "video/webm",
            userResourceService.Object,
            videoTranscoder.Object);
        var result = await service.NormalizeAsync(
            userId,
            workspaceId: null,
            platform: "threads",
            postType: "reels",
            resources: new[] { sourceResource },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TemporaryResourceIds.Should().ContainSingle().Which.Should().Be(derivativeResourceId);
        result.Value.Resources.Should().ContainSingle();
        result.Value.Resources[0].ContentType.Should().Be("video/mp4");
        capturedDataUrl.Should().Be($"data:video/mp4;base64,{Convert.ToBase64String(mp4Bytes)}");
    }

    private static SocialPublishMediaNormalizer CreateService(
        byte[] responseBytes,
        string contentType,
        IUserResourceService userResourceService,
        ISocialPublishVideoTranscoder? videoTranscoder = null)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory
            .Setup(item => item.CreateClient("SocialPublishMedia"))
            .Returns(new HttpClient(new StubHttpMessageHandler(responseBytes, contentType)));

        return new SocialPublishMediaNormalizer(
            factory.Object,
            userResourceService,
            videoTranscoder ?? Mock.Of<ISocialPublishVideoTranscoder>(),
            Mock.Of<ILogger<SocialPublishMediaNormalizer>>());
    }

    private static Task<byte[]> CreateWebpAsync(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        bitmap.Erase(SKColors.DarkSlateBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Webp, quality: 90);
        return Task.FromResult(data.ToArray());
    }

    private static Task<byte[]> CreatePngAsync(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        bitmap.Erase(SKColors.Transparent);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, quality: 100);
        return Task.FromResult(data.ToArray());
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly byte[] _responseBytes;
        private readonly string _contentType;

        public StubHttpMessageHandler(byte[] responseBytes, string contentType)
        {
            _responseBytes = responseBytes;
            _contentType = contentType;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_responseBytes)
                {
                    Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(_contentType) }
                }
            });
        }
    }
}
