using System.Net;
using System.Text;
using Application.Abstractions.ApiCredentials;
using FluentAssertions;
using Infrastructure.Logic.Kie;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace AiMicroservice.Tests.Infrastructure.Logic.Kie;

public sealed class KieResponsesClientTests
{
    [Fact]
    public async Task GetFunctionArgumentsAsync_ShouldReadToolCallsArrayInsideAssistantMessage()
    {
        var responseBody =
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
                        "name": "analyze_schedule_request",
                        "arguments": "{\"action\":\"post_created\"}"
                      }
                    }
                  ]
                }
              ]
            }
            """;

        var client = CreateClient(responseBody);
        var result = await client.GetFunctionArgumentsAsync(
            "gpt-5-4",
            [KieResponsesClient.UserText("analyze this")],
            new KieResponsesFunctionTool
            {
                Name = "analyze_schedule_request",
                Description = "Analyze a request",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        action = new { type = "string" }
                    }
                }
            },
            "Agent.RequestFailed",
            "Kie agent request failed.",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("{\"action\":\"post_created\"}");
    }

    [Fact]
    public async Task GetFunctionArgumentsAsync_ShouldFallbackToJsonText_WhenToolCallIsMissing()
    {
        var responseBody =
            """
            {
              "output": [
                {
                  "type": "message",
                  "role": "assistant",
                  "content": [
                    {
                      "type": "output_text",
                      "text": "{\"is_sensitive\":false,\"category\":null,\"reason\":\"safe\",\"confidence_score\":0.98}"
                    }
                  ]
                }
              ]
            }
            """;

        var client = CreateClient(responseBody);
        var result = await client.GetFunctionArgumentsAsync(
            "gpt-5-4",
            [KieResponsesClient.UserText("moderate this")],
            new KieResponsesFunctionTool
            {
                Name = "report_sensitive_content",
                Description = "Moderate content",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        is_sensitive = new { type = "boolean" }
                    }
                }
            },
            "ContentModeration.RequestFailed",
            "Kie content moderation request failed.",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("\"is_sensitive\":false");
    }

    [Fact]
    public async Task GetFunctionArgumentsAsync_ShouldReadChatCompletionsStyleToolCalls()
    {
        var responseBody =
            """
            {
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "tool_calls": [
                      {
                        "id": "call_456",
                        "type": "function",
                        "function": {
                          "name": "analyze_schedule_request",
                          "arguments": "{\"action\":\"post_created\",\"finalPrompt\":\"future-safe\"}"
                        }
                      }
                    ]
                  }
                }
              ]
            }
            """;

        var client = CreateClient(responseBody);
        var result = await client.GetFunctionArgumentsAsync(
            "gpt-5-4",
            [KieResponsesClient.UserText("analyze this")],
            new KieResponsesFunctionTool
            {
                Name = "analyze_schedule_request",
                Description = "Analyze a request",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        action = new { type = "string" },
                        finalPrompt = new { type = "string" }
                    }
                }
            },
            "Agent.RequestFailed",
            "Kie agent request failed.",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("\"action\":\"post_created\"");
    }

    [Fact]
    public async Task GetFunctionArgumentsAsync_ShouldSendAutoToolChoice_ForFunctionTools()
    {
        const string responseBody =
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
                        "name": "analyze_schedule_request",
                        "arguments": "{\"action\":\"post_created\"}"
                      }
                    }
                  ]
                }
              ]
            }
            """;

        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        });
        var client = CreateClient(handler);

        var result = await client.GetFunctionArgumentsAsync(
            "gpt-5-4",
            [KieResponsesClient.UserText("analyze this")],
            new KieResponsesFunctionTool
            {
                Name = "analyze_schedule_request",
                Description = "Analyze a request",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        action = new { type = "string" }
                    }
                }
            },
            "Agent.RequestFailed",
            "Kie agent request failed.",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        handler.LastRequestBody.Should().NotBeNull();
        handler.LastRequestBody.Should().Contain("\"tool_choice\":\"auto\"");
    }

    [Theory]
    [InlineData("gpt-5-4", null, "gpt-5-4")]
    [InlineData("gpt-5.3-codex", null, "gpt-5.3-codex")]
    [InlineData("gemini-3.1-flash-lite-preview", "gpt-5-5", "gpt-5-5")]
    [InlineData("gemini-3.1-flash-lite-preview", null, "gpt-5-4")]
    public void ResolveResponsesModel_ShouldFallback_WhenPreferredModelIsUnsupported(
        string? preferredModel,
        string? configuredModel,
        string expected)
    {
        var resolved = KieResponsesClient.ResolveResponsesModel(preferredModel, configuredModel);

        resolved.Should().Be(expected);
    }

    private static KieResponsesClient CreateClient(string responseBody)
    {
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        });

        return CreateClient(handler);
    }

    private static KieResponsesClient CreateClient(StubHttpMessageHandler handler)
    {

        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(factory => factory.CreateClient("KieChat"))
            .Returns(new HttpClient(handler));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kie:ApiKey"] = "unit-test-key",
                ["Kie:BaseUrl"] = "https://unit.test"
            })
            .Build();

        var credentialProvider = new Mock<IApiCredentialProvider>();
        credentialProvider
            .Setup(provider => provider.GetRequiredValue("Kie", "ApiKey"))
            .Returns("unit-test-key");

        return new KieResponsesClient(
            configuration,
            httpClientFactory.Object,
            credentialProvider.Object,
            Mock.Of<ILogger<KieResponsesClient>>());
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        public string? LastRequestBody { get; private set; }

        public StubHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content is null
                ? null
                : request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            return Task.FromResult(_response);
        }
    }
}
