using Application.Abstractions.Billing;
using Application.Abstractions.Configs;
using Application.Abstractions.Rag;
using Application.Abstractions.Resources;
using Application.Billing;
using Application.Posts.Commands;
using Domain.Entities;
using Domain.Repositories;
using FluentAssertions;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace AiMicroservice.Tests.Application.Posts.Commands;

public sealed class GenerateSocialMediaCaptionsCommandTests
{
    [Fact]
    public async Task Handle_ShouldGenerateSingleCaptionPostUsingKnowledgeAndGpt4o()
    {
        var userId = Guid.NewGuid();
        var postId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();

        var postRepository = new Mock<IPostRepository>();
        var userResourceService = new Mock<IUserResourceService>();
        var userConfigService = new Mock<IUserConfigService>();
        var ragClient = new Mock<IRagClient>();
        var multimodalLlm = new Mock<IMultimodalLlmClient>();
        var pricingService = new Mock<ICoinPricingService>();
        var billingClient = new Mock<IBillingClient>();
        var aiSpendRecordRepository = new Mock<IAiSpendRecordRepository>();

        postRepository
            .Setup(repository => repository.GetByIdAsync(postId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Post
            {
                Id = postId,
                UserId = userId,
                Title = "Launch teaser",
                Content = new PostContent { Content = "Short product reveal", PostType = "post" }
            });

        userResourceService
            .Setup(service => service.GetPresignedResourcesAsync(
                userId,
                It.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { resourceId })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<UserResourcePresignResult>>(
            [
                new UserResourcePresignResult(
                    resourceId,
                    "https://cdn.example.com/resource-1.jpg",
                    "image/jpeg",
                    "image")
            ]));

        userConfigService
            .Setup(service => service.GetActiveConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<UserAiConfig?>(new UserAiConfig(
                Guid.NewGuid(),
                "ignored-caption-model",
                null,
                4)));

        ragClient
            .Setup(client => client.WaitForRagReadyAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        ragClient
            .Setup(client => client.QueryAsync(
                It.Is<RagQueryRequest>(request =>
                    request.DocumentIdPrefix == "knowledge:" &&
                    request.OnlyNeedContext &&
                    request.TopK == 8 &&
                    request.Query.Contains("Launch teaser")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagQueryResponse(
                "caption guidance",
                "hybrid",
                8,
                "Brand voice: concise, confident, product-led.",
                ["knowledge:brand:voice"]));

        ragClient
            .Setup(client => client.QueryAsync(
                It.Is<RagQueryRequest>(request =>
                    request.DocumentIdPrefix == "knowledge:image-design-marketing:" &&
                    request.OnlyNeedContext &&
                    request.Mode == "naive" &&
                    request.Query.Contains("marketing")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagQueryResponse(
                "image design rules for marketing style social media post",
                "naive",
                8,
                "Marketing style requires direct CTA and promotional framing.",
                ["knowledge:image-design-marketing:rules"]));

        multimodalLlm
            .Setup(client => client.GenerateAnswerAsync(
                It.Is<MultimodalAnswerRequest>(request =>
                    request.ModelOverride == "openai/gpt-4o" &&
                    request.MaxOutputTokens == 500 &&
                    request.WebSearchEnabled == true &&
                    request.ReferenceImageUrls != null &&
                    request.ReferenceImageUrls.SequenceEqual(new[] { "https://cdn.example.com/resource-1.jpg" }) &&
                    request.UserText.Contains("Brand voice") &&
                    request.UserText.Contains("Caption style: marketing") &&
                    request.UserText.Contains("Web search: enabled") &&
                    request.UserText.Contains("Marketing style requires direct CTA")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MultimodalAnswerResult(
                """
                {"captions":[{"caption":"TikTok launch caption","hashtags":["#Launch","#AI"],"trendingHashtags":["#ForYou"],"callToAction":"Try it now"}]}
                """,
                []));

        pricingService
            .Setup(service => service.GetCostAsync(
                CoinActionTypes.CaptionGeneration,
                "openai/gpt-4o",
                null,
                1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new CoinCostQuote(
                CoinActionTypes.CaptionGeneration,
                "openai/gpt-4o",
                null,
                "per_platform",
                3m,
                1,
                3m)));

        billingClient
            .Setup(client => client.DebitAsync(
                userId,
                3m,
                CoinDebitReasons.CaptionGenerationDebit,
                CoinReferenceTypes.CaptionBatch,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(100m));

        aiSpendRecordRepository
            .Setup(repository => repository.AddAsync(
                It.Is<AiSpendRecord>(record =>
                    record.Provider == AiSpendProviders.OpenRouter &&
                    record.Model == "openai/gpt-4o" &&
                    record.Quantity == 1),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        aiSpendRecordRepository
            .Setup(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = CreateHandler(
            postRepository,
            userResourceService,
            userConfigService,
            ragClient,
            multimodalLlm,
            pricingService,
            billingClient,
            aiSpendRecordRepository);

        var result = await handler.Handle(
            new GenerateSocialMediaCaptionsCommand(
                userId,
                new SocialMediaCaptionPostInput(postId, "TikTok", [resourceId]),
                "en",
                "Keep it energetic",
                500,
                "marketting",
                true),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.SocialMedia.Should().HaveCount(1);
        result.Value.SocialMedia[0].PostId.Should().Be(postId);
        result.Value.SocialMedia[0].SocialMediaType.Should().Be("tiktok");
        result.Value.SocialMedia[0].ResourceList.Should().Equal(resourceId);
        result.Value.SocialMedia[0].Captions[0].Caption.Should().Be("TikTok launch caption");
        result.Value.SocialMedia[0].Captions[0].Hashtags.Should().Equal("#Launch", "#AI");
        result.Value.SocialMedia[0].Captions[0].TrendingHashtags.Should().Equal("#ForYou");
        result.Value.SocialMedia[0].Captions[0].CallToAction.Should().Be("Try it now");
    }

    [Fact]
    public async Task Handle_ShouldFailForUnsupportedPlatform()
    {
        var postRepository = new Mock<IPostRepository>();
        var userResourceService = new Mock<IUserResourceService>();
        var userConfigService = new Mock<IUserConfigService>();
        var ragClient = new Mock<IRagClient>();
        var multimodalLlm = new Mock<IMultimodalLlmClient>();
        var pricingService = new Mock<ICoinPricingService>();
        var billingClient = new Mock<IBillingClient>();
        var aiSpendRecordRepository = new Mock<IAiSpendRecordRepository>();

        var handler = CreateHandler(
            postRepository,
            userResourceService,
            userConfigService,
            ragClient,
            multimodalLlm,
            pricingService,
            billingClient,
            aiSpendRecordRepository);

        var result = await handler.Handle(
            new GenerateSocialMediaCaptionsCommand(
                Guid.NewGuid(),
                new SocialMediaCaptionPostInput(Guid.NewGuid(), "YouTube", [Guid.NewGuid()]),
                "en",
                null,
                null,
                null,
                false),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SocialMedia.UnsupportedPlatform");
        ragClient.VerifyNoOtherCalls();
        multimodalLlm.VerifyNoOtherCalls();
        pricingService.VerifyNoOtherCalls();
    }

    private static GenerateSocialMediaCaptionsCommandHandler CreateHandler(
        Mock<IPostRepository> postRepository,
        Mock<IUserResourceService> userResourceService,
        Mock<IUserConfigService> userConfigService,
        Mock<IRagClient> ragClient,
        Mock<IMultimodalLlmClient> multimodalLlm,
        Mock<ICoinPricingService> pricingService,
        Mock<IBillingClient> billingClient,
        Mock<IAiSpendRecordRepository> aiSpendRecordRepository)
    {
        return new GenerateSocialMediaCaptionsCommandHandler(
            postRepository.Object,
            userResourceService.Object,
            userConfigService.Object,
            ragClient.Object,
            multimodalLlm.Object,
            pricingService.Object,
            billingClient.Object,
            aiSpendRecordRepository.Object);
    }
}
