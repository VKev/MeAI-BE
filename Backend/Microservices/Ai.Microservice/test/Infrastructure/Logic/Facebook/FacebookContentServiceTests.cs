using System.Net;
using System.Text;
using Application.Abstractions.Facebook;
using FluentAssertions;
using Infrastructure.Logic.Facebook;
using Moq;

namespace AiMicroservice.Tests.Infrastructure.Logic.Facebook;

public sealed class FacebookContentServiceTests
{
    [Fact]
    public async Task GetPostAsync_ShouldFallbackToInsightsWhenEngagementEdgesAreBlocked()
    {
        var service = CreateService(static request =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/me/accounts", StringComparison.Ordinal))
            {
                return JsonResponse("""
                    {
                      "data": [
                        {
                          "id": "123",
                          "access_token": "page-token"
                        }
                      ]
                    }
                    """);
            }

            if (path.EndsWith("/123_456/comments", StringComparison.Ordinal) ||
                path.EndsWith("/123_456/reactions", StringComparison.Ordinal))
            {
                return JsonResponse(
                    """
                    {
                      "error": {
                        "message": "(#10) This endpoint requires the 'pages_read_engagement' permission or the 'Page Public Content Access' feature.",
                        "code": 10
                      }
                    }
                    """,
                    HttpStatusCode.BadRequest);
            }

            if (path.EndsWith("/123_456/insights", StringComparison.Ordinal))
            {
                return JsonResponse("""
                    {
                      "data": [
                        {
                          "name": "post_impressions_unique",
                          "values": [
                            {
                              "value": 1200
                            }
                          ]
                        },
                        {
                          "name": "post_reactions_by_type_total",
                          "values": [
                            {
                              "value": {
                                "like": 20,
                                "love": 5
                              }
                            }
                          ]
                        },
                        {
                          "name": "post_activity_by_action_type",
                          "values": [
                            {
                              "value": {
                                "comment": 7,
                                "share": 3
                              }
                            }
                          ]
                        }
                      ]
                    }
                    """);
            }

            if (path.EndsWith("/123_456", StringComparison.Ordinal))
            {
                return JsonResponse("""
                    {
                      "id": "123_456",
                      "message": "Launch update",
                      "created_time": "2026-03-18T09:00:00+0000",
                      "permalink_url": "https://facebook.com/123_456",
                      "full_picture": "https://cdn.example.com/full.jpg",
                      "attachments": {
                        "data": [
                          {
                            "media_type": "photo",
                            "type": "photo",
                            "url": "https://facebook.com/photo.php?fbid=456",
                            "title": "Campaign launch",
                            "description": "Description",
                            "media": {
                              "image": {
                                "src": "https://cdn.example.com/thumb.jpg"
                              }
                            },
                            "target": {
                              "id": "asset-1"
                            }
                          }
                        ]
                      }
                    }
                    """);
            }

            throw new InvalidOperationException($"Unexpected request: {request.RequestUri}");
        });

        var result = await service.GetPostAsync(
            new FacebookPostDetailsRequest(
                UserAccessToken: "user-token",
                PostId: "123_456"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(new FacebookPostDetails(
            Id: "123_456",
            PageId: "123",
            Message: "Launch update",
            Story: null,
            PermalinkUrl: "https://facebook.com/123_456",
            CreatedTime: "2026-03-18T09:00:00+0000",
            FullPictureUrl: "https://cdn.example.com/full.jpg",
            MediaType: "image",
            MediaUrl: "https://facebook.com/photo.php?fbid=456",
            ThumbnailUrl: "https://cdn.example.com/thumb.jpg",
            AttachmentTitle: "Campaign launch",
            AttachmentDescription: "Description",
            ViewCount: null,
            ReactionCount: 25,
            CommentCount: 7,
            ShareCount: 3,
            ReactionBreakdown: new Dictionary<string, long>
            {
                ["like"] = 20,
                ["love"] = 5
            },
            ReachCount: 1200,
            ImpressionCount: null));
    }

    [Fact]
    public async Task GetPostsAsync_ShouldQueryOnlySafeBaseFields()
    {
        string? publishedPostsQuery = null;
        var service = CreateService(request =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/me/accounts", StringComparison.Ordinal))
            {
                return JsonResponse("""
                    {
                      "data": [
                        {
                          "id": "123",
                          "access_token": "page-token"
                        }
                      ]
                    }
                    """);
            }

            if (path.EndsWith("/123/published_posts", StringComparison.Ordinal))
            {
                publishedPostsQuery = Uri.UnescapeDataString(request.RequestUri!.Query);

                return JsonResponse("""
                    {
                      "data": [
                        {
                          "id": "123_456",
                          "message": "Launch update",
                          "created_time": "2026-03-18T09:00:00+0000",
                          "permalink_url": "https://facebook.com/123_456",
                          "full_picture": "https://cdn.example.com/full.jpg",
                          "shares": {
                            "count": 3
                          }
                        }
                      ]
                    }
                    """);
            }

            throw new InvalidOperationException($"Unexpected request: {request.RequestUri}");
        });

        var result = await service.GetPostsAsync(
            new FacebookPostListRequest(
                UserAccessToken: "user-token",
                Limit: 10),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Posts.Should().ContainSingle();

        publishedPostsQuery.Should().NotBeNull();
        publishedPostsQuery!.Should().Contain("shares");
        publishedPostsQuery.Should().Contain("attachments{");
        publishedPostsQuery.Should().NotContain("comments.limit(0).summary(true)");
        publishedPostsQuery.Should().NotContain("reactions.limit(0).summary(true)");
        publishedPostsQuery.Should().NotContain("object_id");
    }

    private static IFacebookContentService CreateService(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var factory = new Mock<IHttpClientFactory>();
        var client = new HttpClient(new StubHttpMessageHandler(responder))
        {
            BaseAddress = new Uri("https://graph.facebook.com")
        };

        factory
            .Setup(item => item.CreateClient("Facebook"))
            .Returns(client);

        return new FacebookContentService(factory.Object);
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
