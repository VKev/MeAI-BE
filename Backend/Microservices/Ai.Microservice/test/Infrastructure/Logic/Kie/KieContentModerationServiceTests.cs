using System.Net;
using System.Text;
using Application.Abstractions.ApiCredentials;
using Application.Abstractions.Gemini;
using FluentAssertions;
using Infrastructure.Logic.Kie;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace AiMicroservice.Tests.Infrastructure.Logic.Kie;

public sealed class KieContentModerationServiceTests
{
    [Fact]
    public async Task CheckSensitiveContentAsync_ShouldFallbackToJsonOnlyMode_WhenToolCallIsMissing()
    {
        var handler = new QueueHttpMessageHandler(
        [
            CreateResponse(
                """
                {
                  "output": [
                    {
                      "type": "message",
                      "role": "assistant",
                      "content": [
                        {
                          "type": "output_text",
                          "text": "This post looks safe."
                        }
                      ]
                    }
                  ]
                }
                """),
            CreateResponse(
                """
                {
                  "output": [
                    {
                      "type": "message",
                      "role": "assistant",
                      "content": [
                        {
                          "type": "output_text",
                          "text": "{\"is_sensitive\":false,\"category\":null,\"reason\":\"safe\",\"confidence_score\":0.92}"
                        }
                      ]
                    }
                  ]
                }
                """)
        ]);

        var service = CreateService(handler);

        var result = await service.CheckSensitiveContentAsync(
            new ContentModerationRequest("Bai viet gioi thieu san pham moi."),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsSensitive.Should().BeFalse();
        result.Value.Category.Should().BeNull();
        result.Value.Reason.Should().Be("safe");
        result.Value.ConfidenceScore.Should().Be(0.92);
        handler.RequestBodies.Should().HaveCount(2);
        handler.RequestBodies[0].Should().Contain("\"report_sensitive_content\"");
        handler.RequestBodies[1].Should().Contain("Return JSON only");
        handler.RequestBodies[1].Should().NotContain("\"report_sensitive_content\"");
    }

    [Fact]
    public async Task CheckSensitiveContentAsync_ShouldUseFunctionResult_WhenToolCallSucceeds()
    {
        var handler = new QueueHttpMessageHandler(
        [
            CreateResponse(
                """
                {
                  "output": [
                    {
                      "type": "message",
                      "role": "assistant",
                      "tool_calls": [
                        {
                          "id": "call_123",
                          "type": "function",
                          "function": {
                            "name": "report_sensitive_content",
                            "arguments": "{\"is_sensitive\":true,\"category\":\"violence\",\"reason\":\"graphic injury\",\"confidence_score\":0.81}"
                          }
                        }
                      ]
                    }
                  ]
                }
                """)
        ]);

        var service = CreateService(handler);

        var result = await service.CheckSensitiveContentAsync(
            new ContentModerationRequest("Noi dung nhay cam."),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsSensitive.Should().BeTrue();
        result.Value.Category.Should().Be("violence");
        result.Value.Reason.Should().Be("graphic injury");
        result.Value.ConfidenceScore.Should().Be(0.81);
        handler.RequestBodies.Should().HaveCount(1);
    }

    private static KieContentModerationService CreateService(QueueHttpMessageHandler handler)
    {
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(factory => factory.CreateClient("KieChat"))
            .Returns(new HttpClient(handler));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kie:ApiKey"] = "unit-test-key",
                ["Kie:BaseUrl"] = "https://unit.test",
                ["Kie:ChatModel"] = "gpt-5-4"
            })
            .Build();

        var credentialProvider = new Mock<IApiCredentialProvider>();
        credentialProvider
            .Setup(provider => provider.GetRequiredValue("Kie", "ApiKey"))
            .Returns("unit-test-key");

        var client = new KieResponsesClient(
            configuration,
            httpClientFactory.Object,
            credentialProvider.Object,
            Mock.Of<ILogger<KieResponsesClient>>());

        return new KieContentModerationService(
            configuration,
            client,
            Mock.Of<ILogger<KieContentModerationService>>());
    }

    private static HttpResponseMessage CreateResponse(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private sealed class QueueHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public QueueHttpMessageHandler(IEnumerable<HttpResponseMessage> responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        public List<string> RequestBodies { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                RequestBodies.Add(request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult());
            }

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No queued HTTP response available for this request.");
            }

            return Task.FromResult(_responses.Dequeue());
        }
    }
}
