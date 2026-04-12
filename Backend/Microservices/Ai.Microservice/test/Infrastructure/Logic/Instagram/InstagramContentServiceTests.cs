using System.Net;
using System.Text;
using Application.Abstractions.Instagram;
using FluentAssertions;
using Infrastructure.Logic.Instagram;
using Moq;

namespace AiMicroservice.Tests.Infrastructure.Logic.Instagram;

public sealed class InstagramContentServiceTests
{
    [Fact]
    public async Task GetPostInsightsAsync_ShouldSkipViewsMetric_ForImagePosts()
    {
        string? requestedMetrics = null;
        var service = CreateService(request =>
        {
            requestedMetrics = GetMetricQuery(request.RequestUri!);

            return JsonResponse("""
                {
                  "data": [
                    { "name": "reach", "values": [{ "value": 120 }] },
                    { "name": "impressions", "values": [{ "value": 180 }] },
                    { "name": "saved", "values": [{ "value": 4 }] },
                    { "name": "shares", "values": [{ "value": 2 }] }
                  ]
                }
                """);
        });

        var result = await service.GetPostInsightsAsync(
            new InstagramPostInsightsRequest(
                AccessToken: "ig-token",
                PostId: "ig-post-1",
                MediaType: "IMAGE",
                MediaProductType: "FEED"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        requestedMetrics.Should().Be("reach,impressions,saved,shares");
        result.Value.Should().BeEquivalentTo(new InstagramPostInsights(
            Views: null,
            Reach: 120,
            Impressions: 180,
            Saved: 4,
            Shares: 2));
    }

    [Fact]
    public async Task GetPostInsightsAsync_ShouldRetryWithoutViews_WhenInitialMetricSetFails()
    {
        var requestedMetrics = new List<string>();
        var service = CreateService(request =>
        {
            var metrics = GetMetricQuery(request.RequestUri!);
            requestedMetrics.Add(metrics);

            if (string.Equals(metrics, "views,reach,impressions,saved,shares", StringComparison.Ordinal))
            {
                return JsonResponse(
                    """
                    {
                      "error": {
                        "message": "(#100) The metric 'views' is not available for this media."
                      }
                    }
                    """,
                    HttpStatusCode.BadRequest);
            }

            return JsonResponse("""
                {
                  "data": [
                    { "name": "reach", "values": [{ "value": 220 }] },
                    { "name": "impressions", "values": [{ "value": 300 }] },
                    { "name": "saved", "values": [{ "value": 5 }] },
                    { "name": "shares", "values": [{ "value": 3 }] }
                  ]
                }
                """);
        });

        var result = await service.GetPostInsightsAsync(
            new InstagramPostInsightsRequest(
                AccessToken: "ig-token",
                PostId: "ig-post-2",
                MediaType: "VIDEO",
                MediaProductType: "REELS"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        requestedMetrics.Should().ContainInOrder(
            "views,reach,impressions,saved,shares",
            "reach,impressions,saved,shares");
        result.Value.Should().BeEquivalentTo(new InstagramPostInsights(
            Views: null,
            Reach: 220,
            Impressions: 300,
            Saved: 5,
            Shares: 3));
    }

    private static IInstagramContentService CreateService(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var factory = new Mock<IHttpClientFactory>();
        var client = new HttpClient(new StubHttpMessageHandler(responder))
        {
            BaseAddress = new Uri("https://graph.facebook.com")
        };

        factory
            .Setup(item => item.CreateClient("Instagram"))
            .Returns(client);

        return new InstagramContentService(factory.Object);
    }

    private static string GetMetricQuery(Uri uri)
    {
        var query = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                pair => pair[0],
                pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : string.Empty,
                StringComparer.OrdinalIgnoreCase);

        return query["metric"];
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
