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
