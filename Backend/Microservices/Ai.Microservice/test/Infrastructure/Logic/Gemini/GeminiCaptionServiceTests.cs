using System.Net;
using System.Text;
using Application.Abstractions.Gemini;
using FluentAssertions;
using Infrastructure.Logic.Gemini;
using Microsoft.Extensions.Configuration;
using Moq;

namespace AiMicroservice.Tests.Infrastructure.Logic.Gemini;

public sealed class GeminiCaptionServiceTests
{
    [Fact]
    public async Task GenerateSocialMediaCaptionsAsync_ShouldParseStructuredJsonResponse()
    {
        var responseBody =
            """
            {
              "candidates": [
                {
                  "content": {
                    "parts": [
                      {
                        "text": "{\"captions\":[{\"caption\":\"TikTok launch caption\",\"hashtags\":[\"#Launch\",\"#AI\"],\"trendingHashtags\":[\"#ForYou\",\"#TechTok\"],\"callToAction\":\"Try it now\"}]}"
                      }
                    ]
                  }
                }
              ]
            }
            """;

        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        });

        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(factory => factory.CreateClient("Gemini"))
            .Returns(new HttpClient(handler));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gemini:ApiKey"] = "unit-test-key",
                ["Gemini:BaseUrl"] = "https://unit.test",
                ["Gemini:Model"] = "gemini-test"
            })
            .Build();

        var service = new GeminiCaptionService(configuration, httpClientFactory.Object);

        var result = await service.GenerateSocialMediaCaptionsAsync(
            new GeminiSocialMediaCaptionRequest(
                Array.Empty<GeminiCaptionResource>(),
                new GeminiInlineCaptionResource("image/png", [1, 2, 3, 4]),
                "tiktok",
                ["launch", "product demo"],
                3,
                "English",
                "Keep it energetic"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value[0].Caption.Should().Be("TikTok launch caption");
        result.Value[0].Hashtags.Should().Equal("#Launch", "#AI");
        result.Value[0].TrendingHashtags.Should().Equal("#ForYou", "#TechTok");
        result.Value[0].CallToAction.Should().Be("Try it now");

        handler.LastRequestBody.Should().Contain("\"inline_data\"");
        handler.LastRequestBody.Should().Contain("\"response_mime_type\":\"application/json\"");
    }

    [Fact]
    public async Task GenerateSocialMediaCaptionsAsync_ShouldFallbackWhenProviderIsTemporarilyUnavailable()
    {
        var responseBody =
            """
            {
              "error": {
                "message": "This model is currently experiencing high demand. Please try again later."
              }
            }
            """;

        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        });

        var service = CreateService(handler);

        var result = await service.GenerateSocialMediaCaptionsAsync(
            new GeminiSocialMediaCaptionRequest(
                [new GeminiCaptionResource("https://cdn.example.com/asset.webp", "image/webp")],
                null,
                "threads",
                ["launch teaser"],
                3,
                "English",
                "Keep it punchy"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);
        result.Value[0].Caption.Should().Contain("Launch teaser", Exactly.Once());
        result.Value[0].TrendingHashtags.Should().NotBeEmpty();
        result.Value[0].CallToAction.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GenerateTitleAsync_ShouldFallbackWhenProviderIsTemporarilyUnavailable()
    {
        var responseBody =
            """
            {
              "error": {
                "message": "This model is currently experiencing high demand. Please try again later."
              }
            }
            """;

        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        });

        var service = CreateService(handler);

        var result = await service.GenerateTitleAsync(
            new GeminiTitleRequest(
                "Launch teaser with sharper positioning #Promo",
                "English"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Launch teaser with sharper positioning");
    }

    private static GeminiCaptionService CreateService(HttpMessageHandler handler)
    {
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(factory => factory.CreateClient("Gemini"))
            .Returns(new HttpClient(handler));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gemini:ApiKey"] = "unit-test-key",
                ["Gemini:BaseUrl"] = "https://unit.test",
                ["Gemini:Model"] = "gemini-test"
            })
            .Build();

        return new GeminiCaptionService(configuration, httpClientFactory.Object);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public StubHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        public string LastRequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return _response;
        }
    }
}
