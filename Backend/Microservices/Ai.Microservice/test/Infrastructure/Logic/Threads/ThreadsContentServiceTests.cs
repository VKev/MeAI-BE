using System.Net;
using System.Text;
using Application.Abstractions.Threads;
using FluentAssertions;
using Infrastructure.Logic.Threads;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AiMicroservice.Tests.Infrastructure.Logic.Threads;

public sealed class ThreadsContentServiceTests
{
    [Fact]
    public async Task GetPostsAsync_ShouldMapMixedCarouselChildren()
    {
        string? capturedQuery = null;
        var service = CreateService(request =>
        {
            capturedQuery = Uri.UnescapeDataString(request.RequestUri!.Query);
            return JsonResponse("""
                {
                  "data": [
                    {
                      "id": "thread-1",
                      "media_type": "CAROUSEL_ALBUM",
                      "children": {
                        "data": [
                          {
                            "id": "child-video",
                            "media_type": "VIDEO",
                            "media_url": "https://cdn.example.com/video.mp4"
                          },
                          {
                            "id": "child-image",
                            "media_type": "IMAGE",
                            "media_url": "https://cdn.example.com/image.jpg"
                          }
                        ]
                      }
                    }
                  ],
                  "paging": {}
                }
                """);
        });

        var result = await service.GetPostsAsync(
            new ThreadsPostListRequest("access-token"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        capturedQuery.Should().Contain("children{id,media_type,media_url,thumbnail_url}");
        result.Value.Posts.Should().ContainSingle();
        result.Value.Posts[0].MediaItems.Should().BeEquivalentTo(
        [
            new ThreadsPostMediaItem("https://cdn.example.com/video.mp4", "video"),
            new ThreadsPostMediaItem("https://cdn.example.com/image.jpg", "image")
        ]);
    }

    private static IThreadsContentService CreateService(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var factory = new Mock<IHttpClientFactory>();
        var client = new HttpClient(new StubHttpMessageHandler(responder));

        factory
            .Setup(item => item.CreateClient("Threads"))
            .Returns(client);

        return new ThreadsContentService(factory.Object, NullLogger<ThreadsContentService>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }
}
