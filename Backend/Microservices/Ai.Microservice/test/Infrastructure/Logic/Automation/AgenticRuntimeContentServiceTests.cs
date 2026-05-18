using System.Net;
using System.Text;
using Application.Abstractions.ApiCredentials;
using Application.Abstractions.Automation;
using Application.Abstractions.Configs;
using Application.Abstractions.Resources;
using FluentAssertions;
using Infrastructure.Logic.Automation;
using Infrastructure.Logic.Kie;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace AiMicroservice.Tests.Infrastructure.Logic.Automation;

public sealed class AgenticRuntimeContentServiceTests
{
    [Fact]
    public async Task GeneratePostDraftAsync_ShouldKeepOnlyExplicitResources_AndStripMarkdownFormatting()
    {
        var firstResourceId = Guid.NewGuid();
        var secondResourceId = Guid.NewGuid();

        var service = CreateService(
            $$"""
              {
                "output": [
                  {
                    "type": "message",
                    "role": "assistant",
                    "tool_calls": [
                      {
                        "id": "call_1",
                        "type": "function",
                        "function": {
                          "name": "create_runtime_post_draft",
                          "arguments": "{\"title\":\"# Launch update\",\"content\":\"# Fresh launch\\n- Big feature drop\\nRead [the source](https://example.com) **today**.\",\"hashtag\":\"#AI\",\"postType\":\"posts\",\"resourceIds\":[\"{{firstResourceId}}\"]}"
                        }
                      }
                    ]
                  }
                ]
              }
              """);

        var result = await service.GeneratePostDraftAsync(
            BuildRequest(
                [
                    new ImportedResourceItem(
                        firstResourceId,
                        "https://cdn.example.com/1.jpg",
                        "image/jpeg",
                        "image",
                        "https://example.com/1.jpg",
                        "https://example.com/article"),
                    new ImportedResourceItem(
                        secondResourceId,
                        "https://cdn.example.com/2.jpg",
                        "image/jpeg",
                        "image",
                        "https://example.com/2.jpg",
                        "https://example.com/article")
                ]),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Title.Should().Be("Launch update");
        result.Value.Content.Should().Be("Fresh launch\nBig feature drop\nRead the source today.");
        result.Value.ResourceIds.Should().BeEquivalentTo([firstResourceId]);
        result.Value.Resources.Should().BeEquivalentTo([new AgenticRuntimeDraftResource(firstResourceId, "image")]);
    }

    [Fact]
    public async Task GeneratePostDraftAsync_ShouldNotAutoAttachMultipleSearchImages_WhenSelectionIsImplicit()
    {
        var firstResourceId = Guid.NewGuid();
        var secondResourceId = Guid.NewGuid();

        var service = CreateService(
            """
            {
              "output": [
                {
                  "type": "message",
                  "role": "assistant",
                  "tool_calls": [
                    {
                      "id": "call_1",
                      "type": "function",
                      "function": {
                        "name": "create_runtime_post_draft",
                        "arguments": "{\"title\":\"Daily AI brief\",\"content\":\"Plain text update without markdown.\",\"hashtag\":\"#AI\",\"postType\":\"posts\"}"
                      }
                    }
                  ]
                }
              ]
            }
            """);

        var result = await service.GeneratePostDraftAsync(
            BuildRequest(
                [
                    new ImportedResourceItem(
                        firstResourceId,
                        "https://cdn.example.com/1.jpg",
                        "image/jpeg",
                        "image",
                        "https://example.com/1.jpg",
                        "https://example.com/article"),
                    new ImportedResourceItem(
                        secondResourceId,
                        "https://cdn.example.com/2.jpg",
                        "image/jpeg",
                        "image",
                        "https://example.com/2.jpg",
                        "https://example.com/article")
                ]),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ResourceIds.Should().BeNull();
        result.Value.Resources.Should().BeNull();
    }

    private static AgenticRuntimeContentRequest BuildRequest(
        IReadOnlyList<ImportedResourceItem> importedResources)
    {
        return new AgenticRuntimeContentRequest(
            Guid.NewGuid(),
            "Launch update",
            "Write a launch update",
            "facebook",
            280,
            new AgentWebSearchResponse(
                "latest AI launch",
                DateTime.UtcNow,
                [
                    new AgentWebSearchResultItem(
                        "Launch article",
                        "https://example.com/article",
                        "Launch summary",
                        "search",
                        "Launch article",
                        "Launch summary")
                ],
                "Launch summary",
                importedResources),
            Guid.NewGuid(),
            Guid.NewGuid());
    }

    private static AgenticRuntimeContentService CreateService(string kieResponseBody)
    {
        var configuration = new ConfigurationBuilder().Build();

        var credentialProvider = new Mock<IApiCredentialProvider>(MockBehavior.Strict);
        credentialProvider
            .Setup(provider => provider.GetRequiredValue("Kie", "ApiKey"))
            .Returns("unit-test-key");

        var httpClientFactory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        httpClientFactory
            .Setup(factory => factory.CreateClient("KieChat"))
            .Returns(new HttpClient(new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(kieResponseBody, Encoding.UTF8, "application/json")
            })));

        var userConfigService = new Mock<IUserConfigService>(MockBehavior.Strict);
        userConfigService
            .Setup(service => service.GetActiveConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<UserAiConfig?>(new UserAiConfig(
                Guid.NewGuid(),
                "gemini-3.1-flash-lite-preview",
                null,
                null)));

        var kieResponsesClient = new KieResponsesClient(
            configuration,
            httpClientFactory.Object,
            credentialProvider.Object,
            Mock.Of<ILogger<KieResponsesClient>>());

        return new AgenticRuntimeContentService(
            configuration,
            kieResponsesClient,
            new Mock<IAgentWebSearchService>(MockBehavior.Strict).Object,
            new Mock<IWebSearchEnrichmentService>(MockBehavior.Strict).Object,
            new Mock<IUserResourceService>(MockBehavior.Strict).Object,
            userConfigService.Object,
            Mock.Of<ILogger<AgenticRuntimeContentService>>());
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public StubHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_response);
        }
    }
}
