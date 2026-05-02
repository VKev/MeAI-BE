using Application.Abstractions.Agents;
using Application.Abstractions.Automation;
using Application.Posts.Commands;
using Application.Posts.Models;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Logic.Agents;
using MediatR;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace AiMicroservice.Tests.Infrastructure.Logic.Agents;

public sealed class ChatWebPostServiceTests
{
    [Fact]
    public async Task CreateDraftAsync_ShouldUseDirectUrls_WhenPromptContainsUrl()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var postId = Guid.NewGuid();
        var postBuilderId = Guid.NewGuid();
        var importedResourceId = Guid.NewGuid();

        var n8nWorkflowClient = new Mock<IN8nWorkflowClient>(MockBehavior.Strict);
        var webSearchEnrichmentService = new Mock<IWebSearchEnrichmentService>(MockBehavior.Strict);
        var runtimeContentService = new Mock<IAgenticRuntimeContentService>(MockBehavior.Strict);
        var mediator = new Mock<IMediator>(MockBehavior.Strict);

        webSearchEnrichmentService
            .Setup(service => service.EnrichUrlsAsync(
                It.Is<IReadOnlyList<string>>(urls => urls.Count == 1 && urls[0] == "https://example.com/article"),
                "Create a post from https://example.com/article",
                userId,
                workspaceId,
                sessionId,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new N8nWebSearchResponse(
                "Create a post from https://example.com/article",
                DateTime.UtcNow,
                [
                    new N8nWebSearchResultItem(
                        "Example article",
                        "https://example.com/article",
                        "Example description",
                        "direct_url",
                        "Example article",
                        "Detailed page content",
                        ["https://example.com/image.jpg"])
                ],
                "Detailed page content",
                [
                    new N8nImportedResourceItem(
                        importedResourceId,
                        "https://cdn.example.com/image.jpg",
                        "image/jpeg",
                        "image",
                        "https://example.com/image.jpg",
                        "https://example.com/article")
                ]));

        runtimeContentService
            .Setup(service => service.GeneratePostDraftAsync(
                It.Is<AgenticRuntimeContentRequest>(request =>
                    request.AgentPrompt == "Create a post from https://example.com/article" &&
                    request.Search.Results.Count == 1),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AgenticRuntimePostDraft(
                "Generated title",
                "Generated content",
                "#example",
                "posts")));

        mediator
            .Setup(service => service.Send(
                It.Is<CreatePostCommand>(command =>
                    command.UserId == userId &&
                    command.ChatSessionId == sessionId &&
                    command.WorkspaceId == workspaceId &&
                    command.Status == "draft" &&
                    command.NewPostBuilderOrigin == PostBuilderOriginKinds.AiOther &&
                    command.Title == "Generated title" &&
                    command.Content != null &&
                    command.Content.Content == "Generated content" &&
                    command.Content.ResourceList != null &&
                    command.Content.ResourceList.Count == 1 &&
                    command.Content.ResourceList[0] == importedResourceId.ToString()),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PostResponse(
                postId,
                userId,
                "user",
                null,
                workspaceId,
                postBuilderId,
                sessionId,
                null,
                "Generated title",
                new PostContent
                {
                    Content = "Generated content",
                    ResourceList = [importedResourceId.ToString()]
                },
                "draft",
                null,
                false,
                [],
                [],
                DateTime.UtcNow,
                DateTime.UtcNow)));

        var service = new ChatWebPostService(
            n8nWorkflowClient.Object,
            webSearchEnrichmentService.Object,
            runtimeContentService.Object,
            mediator.Object);

        var result = await service.CreateDraftAsync(
            new ChatWebPostRequest(
                userId,
                sessionId,
                workspaceId,
                "Create a post from https://example.com/article"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PostId.Should().Be(postId);
        result.Value.PostBuilderId.Should().Be(postBuilderId);
        result.Value.RetrievalMode.Should().Be("direct_url");
        result.Value.SourceUrls.Should().BeEquivalentTo(["https://example.com/article"]);
        result.Value.ImportedResourceIds.Should().BeEquivalentTo([importedResourceId]);

        n8nWorkflowClient.VerifyNoOtherCalls();
        webSearchEnrichmentService.VerifyAll();
        runtimeContentService.VerifyAll();
        mediator.VerifyAll();
    }

    [Fact]
    public async Task CreateDraftAsync_ShouldUseWebSearch_WhenPromptHasNoUrl()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var postId = Guid.NewGuid();
        var postBuilderId = Guid.NewGuid();

        var n8nWorkflowClient = new Mock<IN8nWorkflowClient>(MockBehavior.Strict);
        var webSearchEnrichmentService = new Mock<IWebSearchEnrichmentService>(MockBehavior.Strict);
        var runtimeContentService = new Mock<IAgenticRuntimeContentService>(MockBehavior.Strict);
        var mediator = new Mock<IMediator>(MockBehavior.Strict);

        n8nWorkflowClient
            .Setup(service => service.WebSearchAsync(
                It.Is<N8nWebSearchRequest>(request =>
                    request.QueryTemplate == "Create a post about today's AI news" &&
                    request.Count == 5 &&
                    request.Freshness == "pd" &&
                    request.UserId == userId &&
                    request.WorkspaceId == workspaceId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new N8nWebSearchResponse(
                "Create a post about today's AI news",
                DateTime.UtcNow,
                [
                    new N8nWebSearchResultItem(
                        "AI news",
                        "https://example.com/news",
                        "Latest AI update",
                        "search",
                        "AI news",
                        "Latest AI update full text")
                ],
                "Latest AI update full text",
                [])));

        runtimeContentService
            .Setup(service => service.GeneratePostDraftAsync(
                It.Is<AgenticRuntimeContentRequest>(request =>
                    request.AgentPrompt == "Create a post about today's AI news" &&
                    request.Search.Query == "Create a post about today's AI news"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AgenticRuntimePostDraft(
                null,
                "AI summary content",
                null,
                "posts")));

        mediator
            .Setup(service => service.Send(
                It.Is<CreatePostCommand>(command =>
                    command.Title == "Create a post about today's AI news" &&
                    command.NewPostBuilderOrigin == PostBuilderOriginKinds.AiOther &&
                    command.Content != null &&
                    command.Content.Content == "AI summary content"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PostResponse(
                postId,
                userId,
                "user",
                null,
                workspaceId,
                postBuilderId,
                sessionId,
                null,
                "Create a post about today's AI news",
                new PostContent { Content = "AI summary content" },
                "draft",
                null,
                false,
                [],
                [],
                DateTime.UtcNow,
                DateTime.UtcNow)));

        var service = new ChatWebPostService(
            n8nWorkflowClient.Object,
            webSearchEnrichmentService.Object,
            runtimeContentService.Object,
            mediator.Object);

        var result = await service.CreateDraftAsync(
            new ChatWebPostRequest(
                userId,
                sessionId,
                workspaceId,
                "Create a post about today's AI news"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PostId.Should().Be(postId);
        result.Value.PostBuilderId.Should().Be(postBuilderId);
        result.Value.RetrievalMode.Should().Be("web_search");
        result.Value.SourceUrls.Should().BeEquivalentTo(["https://example.com/news"]);
        result.Value.ImportedResourceIds.Should().BeEmpty();

        n8nWorkflowClient.VerifyAll();
        webSearchEnrichmentService.VerifyNoOtherCalls();
        runtimeContentService.VerifyAll();
        mediator.VerifyAll();
    }
}
