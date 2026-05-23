using System.Net;
using Application.Abstractions.Threads;
using FluentAssertions;
using Infrastructure.Logic.Threads;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AiMicroservice.Tests.Infrastructure.Logic.Threads;

public sealed class ThreadsPublishServiceTests
{
    [Fact]
    public async Task PublishAsync_ShouldCapTextToThreadsLimitBeforeCreatingContainer()
    {
        var capturedCreateText = string.Empty;
        var service = CreateService(async request =>
        {
            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath.EndsWith("/threads", StringComparison.Ordinal) == true)
            {
                var form = await ReadFormAsync(request);
                capturedCreateText = form["text"];

                return JsonResponse("""{"id":"creation-id"}""");
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath.EndsWith("/threads_publish", StringComparison.Ordinal) == true)
            {
                return JsonResponse("""{"id":"post-id"}""");
            }

            if (request.Method == HttpMethod.Get)
            {
                return JsonResponse("""{"permalink":"https://www.threads.net/@meai/post/test"}""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var result = await service.PublishAsync(
            new ThreadsPublishRequest(
                "threads-token",
                "threads-user-id",
                new string('a', 550),
                Media: null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        capturedCreateText.Should().HaveLength(500);
    }

    private static ThreadsPublishService CreateService(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
    {
        var factory = new Mock<IHttpClientFactory>();
        var client = new HttpClient(new StubHttpMessageHandler(responder));
        factory.Setup(instance => instance.CreateClient("Threads")).Returns(client);

        return new ThreadsPublishService(factory.Object, NullLogger<ThreadsPublishService>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };

    private static async Task<Dictionary<string, string>> ReadFormAsync(HttpRequestMessage request)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync();

        return body
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .ToDictionary(
                pair => WebUtility.UrlDecode(pair[0]),
                pair => WebUtility.UrlDecode(pair[1]));
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            _responder(request);
    }
}
