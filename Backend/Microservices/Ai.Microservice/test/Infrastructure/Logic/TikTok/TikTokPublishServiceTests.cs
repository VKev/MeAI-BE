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

            if (path.EndsWith("/status/fetch/", StringComparison.Ordinal))
            {
                return PublishCompleteResponse();
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
    public async Task PublishAsync_ShouldFallbackToFirstOption_WhenCreatorDoesNotAllowSelfOnlyPrivacy()
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

            if (path.EndsWith("/status/fetch/", StringComparison.Ordinal))
            {
                return PublishCompleteResponse();
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
        capturedPrivacyLevel.Should().Be("PUBLIC_TO_EVERYONE");
        result.Value.PublishId.Should().Be("publish-123");
    }

    [Fact]
    public async Task PublishAsync_ShouldFail_WhenCreatorHasNoPrivacyLevelOptions()
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
                        "privacy_level_options": [],
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

    [Fact]
    public async Task PublishCarouselAsync_ShouldTruncateTitleAndPassFullDescription_WhenCaptionIsLong()
    {
        string? capturedTitle = null;
        string? capturedDescription = null;

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

            if (path.EndsWith("/content/init/", StringComparison.Ordinal))
            {
                var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                using var document = JsonDocument.Parse(body);
                capturedTitle = document.RootElement
                    .GetProperty("post_info")
                    .GetProperty("title")
                    .GetString();

                capturedDescription = document.RootElement
                    .GetProperty("post_info")
                    .GetProperty("description")
                    .GetString();

                return JsonResponse("""
                    {
                      "data": {
                        "publish_id": "publish-carousel-123"
                      },
                      "error": {
                        "code": "ok",
                        "message": ""
                      }
                    }
                    """);
            }

            if (path.EndsWith("/status/fetch/", StringComparison.Ordinal))
            {
                return PublishCompleteResponse();
            }

            throw new InvalidOperationException($"Unexpected TikTok API path: {path}");
        });

        var longCaption = "This is a very long caption that spans across multiple characters, possibly exceeding the standard ninety character limit specified by TikTok for photo posts. It also has some hashtags at the end. #Long #Hashtags";
        var imageUrls = new[] { "https://cdn.example.com/img1.jpg", "https://cdn.example.com/img2.jpg" };

        var result = await service.PublishCarouselAsync(
            new TikTokCarouselPublishRequest(
                AccessToken: "access-token",
                OpenId: "open-123",
                Caption: longCaption,
                ImageUrls: imageUrls,
                IsPrivate: true),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        capturedTitle.Should().NotBeNull();
        capturedTitle!.Length.Should().BeLessThanOrEqualTo(83); // 80 + "..."
        capturedTitle.Should().EndWith("...");
        capturedDescription.Should().Be(longCaption);
        result.Value.PublishId.Should().Be("publish-carousel-123");
    }

    [Fact]
    public async Task PublishCarouselAsync_ShouldReturnProcessingFailure_WhenTikTokRejectsMediaAfterInit()
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
                        "privacy_level_options": ["SELF_ONLY"],
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

            if (path.EndsWith("/content/init/", StringComparison.Ordinal))
            {
                return JsonResponse("""
                    {
                      "data": {
                        "publish_id": "publish-carousel-failed"
                      },
                      "error": {
                        "code": "ok",
                        "message": ""
                      }
                    }
                    """);
            }

            if (path.EndsWith("/status/fetch/", StringComparison.Ordinal))
            {
                return JsonResponse("""
                    {
                      "data": {
                        "status": "FAILED",
                        "fail_reason": "file_format_check_failed"
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

        var result = await service.PublishCarouselAsync(
            new TikTokCarouselPublishRequest(
                AccessToken: "access-token",
                OpenId: "open-123",
                Caption: "caption",
                ImageUrls: new[] { "https://cdn.example.com/img.png" },
                IsPrivate: true),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("TikTok.PublishFailed");
        result.Error.Description.Should().Contain("media format");
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

    private static HttpResponseMessage PublishCompleteResponse()
    {
        return JsonResponse("""
            {
              "data": {
                "status": "PUBLISH_COMPLETE"
              },
              "error": {
                "code": "ok",
                "message": ""
              }
            }
            """);
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
