using Application.Abstractions.Rag;
using Application.Posts;
using Application.Posts.Models;
using Application.Posts.Queries;
using Application.Recommendations.Commands;
using Application.Recommendations.Models;
using Application.Recommendations.Queries;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace AiMicroservice.Tests.Application.Recommendations.Queries;

public sealed class GenerateAnalysisSuggestionQueryTests
{
    [Fact]
    public async Task Handle_ShouldReadAnalyticsAndRagThenReturnTextSuggestion()
    {
        var userId = Guid.NewGuid();
        var socialMediaId = Guid.NewGuid();
        var prefix = $"facebook:{socialMediaId:N}:";
        var now = DateTimeOffset.Parse("2026-05-20T10:00:00+00:00");

        var postOneStats = new SocialPlatformPostStatsResponse(
            Views: 1000,
            Reach: 900,
            Impressions: 1200,
            Likes: 40,
            Comments: 8,
            Shares: 5,
            TotalInteractions: 53,
            Saves: 4);
        var postTwoStats = new SocialPlatformPostStatsResponse(
            Views: 400,
            Reach: 380,
            Impressions: 500,
            Likes: 15,
            Comments: 1,
            Shares: 1,
            TotalInteractions: 17,
            Saves: 2);

        var summary = new SocialPlatformDashboardSummaryResponse(
            SocialMediaId: socialMediaId,
            Platform: "facebook",
            FetchedPostCount: 2,
            HasMorePosts: false,
            NextCursor: null,
            LatestPublishedPostId: "post-1",
            LatestPublishedAt: now,
            AggregatedStats: new SocialPlatformPostStatsResponse(
                Views: 1400,
                Likes: 55,
                Comments: 9,
                Shares: 6,
                TotalInteractions: 70),
            LatestAnalysis: SocialPlatformPostAnalysisFactory.Create(postOneStats),
            AccountInsights: new SocialPlatformAccountInsightsResponse(
                AccountId: "page-1",
                AccountName: "MeAI Camera",
                Username: "meaicamera",
                Followers: 5000,
                Following: 10,
                MediaCount: 80),
            Posts:
            [
                new SocialPlatformDashboardPostResponse(
                    new SocialPlatformPostSummaryResponse(
                        PlatformPostId: "post-1",
                        Title: "Launch camera trust angle",
                        Text: "Should buyers trust AI camera images?",
                        Description: null,
                        MediaType: "image",
                        MediaUrl: "https://cdn.example.com/post-1.jpg",
                        ThumbnailUrl: "https://cdn.example.com/post-1-thumb.jpg",
                        Permalink: "https://facebook.com/post-1",
                        ShareUrl: "https://facebook.com/post-1",
                        EmbedUrl: null,
                        DurationSeconds: null,
                        PublishedAt: now,
                        Stats: postOneStats),
                    SocialPlatformPostAnalysisFactory.Create(postOneStats)),
                new SocialPlatformDashboardPostResponse(
                    new SocialPlatformPostSummaryResponse(
                        PlatformPostId: "post-2",
                        Title: "Generic sale",
                        Text: "Buy now",
                        Description: null,
                        MediaType: "image",
                        MediaUrl: "https://cdn.example.com/post-2.jpg",
                        ThumbnailUrl: "https://cdn.example.com/post-2-thumb.jpg",
                        Permalink: "https://facebook.com/post-2",
                        ShareUrl: "https://facebook.com/post-2",
                        EmbedUrl: null,
                        DurationSeconds: null,
                        PublishedAt: now.AddDays(-1),
                        Stats: postTwoStats),
                    SocialPlatformPostAnalysisFactory.Create(postTwoStats))
            ]);

        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        mediator
            .Setup(m => m.Send(
                It.Is<GetSocialMediaDashboardSummaryQuery>(query =>
                    query.UserId == userId &&
                    query.SocialMediaId == socialMediaId &&
                    query.PostLimit == 8),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(summary));
        mediator
            .Setup(m => m.Send(
                It.Is<IndexSocialAccountPostsCommand>(command =>
                    command.UserId == userId &&
                    command.SocialMediaId == socialMediaId &&
                    command.MaxPosts == 20),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new IndexSocialAccountPostsResponse(
                socialMediaId,
                "facebook",
                prefix,
                TotalPostsScanned: 2,
                NewPosts: 2,
                UpdatedPosts: 0,
                UnchangedPosts: 0,
                QueuedTextDocuments: 2,
                QueuedImageDocuments: 1,
                QueuedVideoDocuments: 0,
                QueuedProfileDocuments: 1)));

        var ragClient = new Mock<IRagClient>(MockBehavior.Strict);
        ragClient
            .Setup(client => client.WaitForRagReadyAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        ragClient
            .Setup(client => client.MultimodalQueryAsync(
                It.Is<RagMultimodalQueryRequest>(query =>
                    query.DocumentIdPrefix == prefix &&
                    query.TopK == 7 &&
                    query.Platform == "facebook" &&
                    query.Query.Contains("higher engagement", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagMultimodalQueryResponse(
                Query: "analysis",
                TopK: 7,
                DocumentIdPrefix: prefix,
                Text: new RagTextResults(
                    Context: "Recent account post context with engagement stats.",
                    MatchedDocumentIds: ["facebook-doc-1"],
                    References:
                    [
                        new RagTextReference(
                            DocumentId: "facebook-doc-1",
                            PostId: "post-1",
                            Content: "Post mentions camera authenticity and AI verification.",
                            Caption: "Should buyers trust AI camera images?")
                    ]),
                Visual:
                [
                    new RagVisualHit(
                        DocumentId: "facebook-img-1",
                        Kind: "image",
                        Scope: "account",
                        ImageUrl: "https://cdn.example.com/post-1.jpg",
                        Caption: "Camera authenticity visual",
                        PostId: "post-1",
                        Score: 0.91,
                        MirroredImageUrl: "https://s3.example.com/post-1.jpg")
                ],
                VisualError: null,
                Video: [],
                VideoError: null));
        ragClient
            .Setup(client => client.QueryAsync(
                It.Is<RagQueryRequest>(query => query.DocumentIdPrefix == $"{prefix}profile"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagQueryResponse(
                Query: "profile",
                Mode: "naive",
                TopK: 1,
                Answer: "MeAI Camera sells digital cameras and authenticity tools.",
                MatchedDocumentIds: ["profile"]));
        ragClient
            .Setup(client => client.QueryAsync(
                It.Is<RagQueryRequest>(query => query.DocumentIdPrefix == "knowledge:content-formulas:"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagQueryResponse(
                Query: "formulas",
                Mode: "hybrid",
                TopK: 3,
                Answer: "Use PAS and AIDA when explaining product trust.",
                MatchedDocumentIds: ["formula"]));
        ragClient
            .Setup(client => client.QueryAsync(
                It.Is<RagQueryRequest>(query => query.DocumentIdPrefix == "knowledge:engagement-triggers:"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagQueryResponse(
                Query: "engagement",
                Mode: "hybrid",
                TopK: 3,
                Answer: "Ask a concrete buyer question to invite comments.",
                MatchedDocumentIds: ["engagement"]));
        ragClient
            .Setup(client => client.QueryAsync(
                It.Is<RagQueryRequest>(query => query.DocumentIdPrefix == "knowledge:platform-algorithm-signals:"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagQueryResponse(
                Query: "algorithm",
                Mode: "hybrid",
                TopK: 3,
                Answer: "Facebook rewards early comments and meaningful shares.",
                MatchedDocumentIds: ["algorithm"]));

        var llmClient = new Mock<IMultimodalLlmClient>(MockBehavior.Strict);
        llmClient
            .Setup(client => client.GenerateAnswerAsync(
                It.Is<MultimodalAnswerRequest>(request =>
                    request.UserText.Contains("Launch camera trust angle", StringComparison.Ordinal) &&
                    request.UserText.Contains("Recent account post context", StringComparison.Ordinal) &&
                    request.ReferenceImageUrls != null &&
                    request.ReferenceImageUrls.SequenceEqual(new[] { "https://s3.example.com/post-1.jpg" }) &&
                    request.WebSearchEnabled == true),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MultimodalAnswerResult(
                "Create a trust-building post that compares real vs AI-generated camera images.",
                Array.Empty<WebSource>()));

        var handler = new GenerateAnalysisSuggestionQueryHandler(
            mediator.Object,
            ragClient.Object,
            llmClient.Object,
            NullLogger<GenerateAnalysisSuggestionQueryHandler>.Instance);

        var result = await handler.Handle(
            new GenerateAnalysisSuggestionQuery(
                userId,
                socialMediaId,
                new AnalysisSuggestionRequest(TopK: 7, MaxRagPosts: 20)),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Suggestion.Should().Contain("trust-building post");
        result.Value.DocumentIdPrefix.Should().Be(prefix);
        result.Value.AnalyzedPostCount.Should().Be(2);
        result.Value.AggregatedStats.Views.Should().Be(1400);
        result.Value.References.Should().Contain(reference => reference.Source == "visual");
    }

    [Fact]
    public async Task Handle_ShouldRejectInvalidPeriod()
    {
        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        var ragClient = new Mock<IRagClient>(MockBehavior.Strict);
        var llmClient = new Mock<IMultimodalLlmClient>(MockBehavior.Strict);
        var handler = new GenerateAnalysisSuggestionQueryHandler(
            mediator.Object,
            ragClient.Object,
            llmClient.Object,
            NullLogger<GenerateAnalysisSuggestionQueryHandler>.Instance);

        var result = await handler.Handle(
            new GenerateAnalysisSuggestionQuery(
                Guid.NewGuid(),
                Guid.NewGuid(),
                new AnalysisSuggestionRequest(
                    From: DateTimeOffset.Parse("2026-05-20T00:00:00+00:00"),
                    To: DateTimeOffset.Parse("2026-05-10T00:00:00+00:00"))),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AnalysisSuggest.InvalidPeriod");
        mediator.VerifyNoOtherCalls();
        ragClient.VerifyNoOtherCalls();
        llmClient.VerifyNoOtherCalls();
    }
}
