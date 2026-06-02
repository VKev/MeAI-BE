using System.Net;
using System.Text;
using System.Text.Json;
using Application.Abstractions;
using Application.Abstractions.ApiCredentials;
using FluentAssertions;
using Infrastructure.Configs;
using Infrastructure.Logic.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AiMicroservice.Tests.Infrastructure.Logic.Services;

public sealed class VeoVideoServiceTests
{
    [Theory]
    [InlineData(null, "veo3_fast")]
    [InlineData("lite", "veo3_lite")]
    [InlineData("fast", "veo3_fast")]
    [InlineData("quality", "veo3")]
    public async Task GenerateVideoAsync_ShouldMapVeo31TierToConcreteApiModel(string? variant, string expectedModel)
    {
        var handler = new CaptureHttpMessageHandler();
        var service = CreateService(handler);

        var result = await service.GenerateVideoAsync(new VeoGenerateRequest(
            Prompt: "Animate the launch",
            Model: "veo-3-1",
            Variant: variant,
            Duration: 12));

        result.Success.Should().BeTrue();
        handler.RequestUri!.AbsolutePath.Should().Be("/api/v1/veo/generate");
        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("model").GetString().Should().Be(expectedModel);
        body.RootElement.TryGetProperty("duration", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GenerateVideoAsync_ShouldBuildGrok15PreviewPayload()
    {
        var handler = new CaptureHttpMessageHandler();
        var service = CreateService(handler);

        var result = await service.GenerateVideoAsync(new VeoGenerateRequest(
            Prompt: "Animate the launch",
            Model: "grok-imagine-video-1-5-preview",
            ImageUrls: ["https://assets.test/first.png", "https://assets.test/ignored.png"],
            AspectRatio: "auto",
            Resolution: "720p",
            Duration: 12));

        result.Success.Should().BeTrue();
        handler.RequestUri!.AbsolutePath.Should().Be("/api/v1/jobs/createTask");
        using var body = JsonDocument.Parse(handler.Body!);
        var input = body.RootElement.GetProperty("input");
        input.GetProperty("aspect_ratio").GetString().Should().Be("auto");
        input.GetProperty("resolution").GetString().Should().Be("720p");
        input.GetProperty("duration").GetInt32().Should().Be(12);
        input.GetProperty("image_urls").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Equal("https://assets.test/first.png");
    }

    [Fact]
    public async Task GenerateVideoAsync_ShouldBuildGeminiOmniPayload()
    {
        var handler = new CaptureHttpMessageHandler();
        var service = CreateService(handler);

        var result = await service.GenerateVideoAsync(new VeoGenerateRequest(
            Prompt: "Animate the launch",
            Model: "gemini-omni-video",
            AspectRatio: "9:16",
            Resolution: "4k",
            Duration: 10));

        result.Success.Should().BeTrue();
        handler.RequestUri!.AbsolutePath.Should().Be("/api/v1/jobs/createTask");
        using var body = JsonDocument.Parse(handler.Body!);
        var input = body.RootElement.GetProperty("input");
        input.GetProperty("duration").GetString().Should().Be("10");
        input.GetProperty("resolution").GetString().Should().Be("4k");
        input.GetProperty("aspect_ratio").GetString().Should().Be("9:16");
    }

    [Fact]
    public async Task GenerateVideoAsync_ShouldUseExclusiveSeedanceFrameFieldForSingleImage()
    {
        var handler = new CaptureHttpMessageHandler();
        var service = CreateService(handler);

        var result = await service.GenerateVideoAsync(new VeoGenerateRequest(
            Prompt: "Animate the launch",
            ImageUrls: ["https://assets.test/frame.png"],
            Model: "bytedance/seedance-2"));

        result.Success.Should().BeTrue();
        using var body = JsonDocument.Parse(handler.Body!);
        var input = body.RootElement.GetProperty("input");
        input.GetProperty("first_frame_url").GetString().Should().Be("https://assets.test/frame.png");
        input.TryGetProperty("reference_image_urls", out _).Should().BeFalse();
        input.TryGetProperty("last_frame_url", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GenerateVideoAsync_ShouldBuildSeedanceReferencePayload()
    {
        var handler = new CaptureHttpMessageHandler();
        var service = CreateService(handler);

        var result = await service.GenerateVideoAsync(new VeoGenerateRequest(
            Prompt: "Animate the launch",
            ImageUrls: ["https://assets.test/reference-1.png", "https://assets.test/reference-2.png"],
            Model: "bytedance/seedance-2",
            GenerationType: "REFERENCE_2_VIDEO",
            Resolution: "1080p",
            Duration: 15,
            GenerateAudio: true,
            ReturnLastFrame: true,
            WebSearch: true));

        result.Success.Should().BeTrue();
        using var body = JsonDocument.Parse(handler.Body!);
        var input = body.RootElement.GetProperty("input");
        input.GetProperty("resolution").GetString().Should().Be("1080p");
        input.GetProperty("duration").GetInt32().Should().Be(15);
        input.GetProperty("generate_audio").GetBoolean().Should().BeTrue();
        input.GetProperty("return_last_frame").GetBoolean().Should().BeTrue();
        input.GetProperty("web_search").GetBoolean().Should().BeTrue();
        input.GetProperty("reference_image_urls").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Equal(
                "https://assets.test/reference-1.png",
                "https://assets.test/reference-2.png");
        input.TryGetProperty("first_frame_url", out _).Should().BeFalse();
        input.TryGetProperty("last_frame_url", out _).Should().BeFalse();
    }

    private static VeoVideoService CreateService(CaptureHttpMessageHandler handler)
    {
        var credentialProvider = new Mock<IApiCredentialProvider>();
        credentialProvider
            .Setup(provider => provider.GetOptionalValue("Kie", "ApiKey"))
            .Returns("test-key");

        return new VeoVideoService(
            new HttpClient(handler),
            Options.Create(new VeoOptions
            {
                BaseUrl = "https://api.test",
                CallbackUrl = "https://callback.test"
            }),
            credentialProvider.Object,
            Mock.Of<ILogger<VeoVideoService>>());
    }

    private sealed class CaptureHttpMessageHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"code":200,"msg":"success","data":{"taskId":"task-123"}}""",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
