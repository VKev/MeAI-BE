using System.Net;
using System.Text;
using System.Text.Json;
using Application.Abstractions.TikTok;
using FluentAssertions;
using Infrastructure.Logic.TikTok;
using Microsoft.Extensions.Logging;
using Moq;

namespace AiMicroservice.Tests.Infrastructure.Logic.TikTok;

public sealed class TikTokPublishServiceTests
{
    [Fact]
    public async Task PublishAsync_ShouldForceSelfOnlyPrivacy_WhenRequestAsksForPublic()
    {
        string? capturedPrivacyLevel = null;

        var service = CreateService(request =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/creator_info/query/", StringComparison.Ordinal))
            {
                return JsonResponse("""
                    {
                      "data": {
                        "creator_avatar_url": "https://cdn.example.com/avatar.jpg",
                        "creator_username": "creator",
                        "creator_nickname": "Creator",
                        "privacy_level_options": ["PUBLIC_TO_EVERYONE", "SELF_ONLY"],
                        "comment_disabled": false,
                        "duet_disabled": false,
                        "stitch_disabled": false,
                        "max_video_post_duration_sec": 600
                      },
                      "error": {
                        "code": "ok",
                        "message": ""
                      }
                    }
                    """);
            }

            if (path.EndsWith("/video/init/", StringComparison.Ordinal))
            {
                var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                using var document = JsonDocument.Parse(body);
                capturedPrivacyLevel = document.RootElement
                    .GetProperty("post_info")
                    .GetProperty("privacy_level")
                    .GetString();

                return JsonResponse("""
                    {
                      "data": {
                        "publish_id": "publish-123"
                      },
                      "error": {
                        "code": "ok",
                        "message": ""
                      }
                    }
                    """);
            }

            throw new InvalidOperationException($"Unexpected TikTok API path: {path}");
        });

        var result = await service.PublishAsync(
            new TikTokPublishRequest(
                AccessToken: "access-token",
                OpenId: "open-123",
                Caption: "caption",
                Media: new TikTokPublishMedia("https://cdn.example.com/video.mp4", "video/mp4"),
                IsPrivate: false),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        capturedPrivacyLevel.Should().Be("SELF_ONLY");
        result.Value.PublishId.Should().Be("publish-123");
    }

    [Fact]
    public async Task PublishAsync_ShouldFail_WhenCreatorDoesNotAllowSelfOnlyPrivacy()
    {
        var service = CreateService(request =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/creator_info/query/", StringComparison.Ordinal))
            {
                return JsonResponse("""
                    {
                      "data": {
                        "creator_avatar_url": "https://cdn.example.com/avatar.jpg",
                        "creator_username": "creator",
                        "creator_nickname": "Creator",
                        "privacy_level_options": ["PUBLIC_TO_EVERYONE"],
                        "comment_disabled": false,
                        "duet_disabled": false,
                        "stitch_disabled": false,
                        "max_video_post_duration_sec": 600
                      },
                      "error": {
                        "code": "ok",
                        "message": ""
                      }
                    }
                    """);
            }

            throw new InvalidOperationException($"Unexpected TikTok API path: {path}");
        });

        var result = await service.PublishAsync(
            new TikTokPublishRequest(
                AccessToken: "access-token",
                OpenId: "open-123",
                Caption: "caption",
                Media: new TikTokPublishMedia("https://cdn.example.com/video.mp4", "video/mp4"),
                IsPrivate: false),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("TikTok.PrivateNotSupported");
    }

    private static ITikTokPublishService CreateService(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var factory = new Mock<IHttpClientFactory>();
        var client = new HttpClient(new StubHttpMessageHandler(responder))
        {
            BaseAddress = new Uri("https://open.tiktokapis.com")
        };

        factory
            .Setup(item => item.CreateClient("TikTok"))
            .Returns(client);

        return new TikTokPublishService(
            factory.Object,
            Mock.Of<ILogger<TikTokPublishService>>());
    }

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }
}
