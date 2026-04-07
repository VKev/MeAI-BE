using System.Net;
using System.Text;
using Application.Abstractions.TikTok;
using FluentAssertions;
using Infrastructure.Logic.TikTok;
using Moq;

namespace AiMicroservice.Tests.Infrastructure.Logic.TikTok;

public sealed class TikTokContentServiceTests
{
    [Fact]
    public async Task GetAccountInsightsAsync_ShouldUseGetRequest()
    {
        HttpMethod? capturedMethod = null;
        string? capturedQuery = null;

        var service = CreateService(request =>
        {
            capturedMethod = request.Method;
            capturedQuery = Uri.UnescapeDataString(request.RequestUri!.Query);

            return JsonResponse("""
                {
                  "data": {
                    "user": {
                      "open_id": "open-123",
                      "display_name": "Creator",
                      "avatar_url": "https://cdn.example.com/avatar.jpg",
                      "bio_description": "Bio",
                      "follower_count": 321,
                      "following_count": 12,
                      "likes_count": 456,
                      "video_count": 7
                    }
                  },
                  "error": {
                    "code": "ok",
                    "message": ""
                  }
                }
                """);
        });

        var result = await service.GetAccountInsightsAsync(
            new TikTokAccountInsightsRequest("access-token"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        capturedMethod.Should().Be(HttpMethod.Get);
        capturedQuery.Should().Contain("likes_count");
        result.Value.Should().BeEquivalentTo(new TikTokAccountInsights(
            OpenId: "open-123",
            DisplayName: "Creator",
            AvatarUrl: "https://cdn.example.com/avatar.jpg",
            BioDescription: "Bio",
            FollowerCount: 321,
            FollowingCount: 12,
            LikesCount: 456,
            VideoCount: 7));
    }

    private static ITikTokContentService CreateService(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var factory = new Mock<IHttpClientFactory>();
        var client = new HttpClient(new StubHttpMessageHandler(responder))
        {
            BaseAddress = new Uri("https://open.tiktokapis.com")
        };

        factory
            .Setup(item => item.CreateClient("TikTok"))
            .Returns(client);

        return new TikTokContentService(factory.Object);
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
