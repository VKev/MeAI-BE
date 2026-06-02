using Application.Abstractions.Instagram;
using FluentAssertions;
using Infrastructure.Logic.Instagram;
using Moq;

namespace AiMicroservice.Tests.Infrastructure.Logic.Instagram;

public sealed class InstagramPublishServiceTests
{
    [Fact]
    public async Task DeleteAsync_ShouldReturnManualDeleteErrorWithoutCallingGraphApi()
    {
        var requestCount = 0;
        var factory = new Mock<IHttpClientFactory>();
        factory
            .Setup(instance => instance.CreateClient("Instagram"))
            .Returns(new HttpClient(new StubHttpMessageHandler(() => requestCount++)));

        var service = new InstagramPublishService(factory.Object);

        var result = await service.DeleteAsync(
            new InstagramDeleteRequest("instagram-media-id", "access-token"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Instagram.DeleteNotSupported");
        requestCount.Should().Be(0);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Action _onSend;

        public StubHttpMessageHandler(Action onSend)
        {
            _onSend = onSend;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _onSend();
            throw new InvalidOperationException($"Unexpected Instagram API request: {request.RequestUri}");
        }
    }
}
