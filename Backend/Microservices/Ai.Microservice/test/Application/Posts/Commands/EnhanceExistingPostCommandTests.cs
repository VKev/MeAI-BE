using Application.Abstractions.Billing;
using Application.Abstractions.Configs;
using Application.Abstractions.Gemini;
using Application.Abstractions.Resources;
using Application.Billing;
using Application.Posts;
using Application.Posts.Commands;
using Application.Posts.Models;
using Domain.Entities;
using Domain.Repositories;
using FluentAssertions;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace AiMicroservice.Tests.Application.Posts.Commands;

public sealed class EnhanceExistingPostCommandTests
{
    [Fact]
    public async Task Handle_ShouldEnhanceTextOnlyPost_WhenNoResourcesProvided()
    {
        var userId = Guid.NewGuid();
        var postId = Guid.NewGuid();

        var postRepository = new Mock<IPostRepository>();
        var userResourceService = new Mock<IUserResourceService>(MockBehavior.Strict);
        var userConfigService = new Mock<IUserConfigService>();
        var geminiCaptionService = new Mock<IGeminiCaptionService>();
        var pricingService = new Mock<ICoinPricingService>();
        var billingClient = new Mock<IBillingClient>();
        var aiSpendRecordRepository = new Mock<IAiSpendRecordRepository>();

        postRepository
            .Setup(repository => repository.GetByIdAsync(postId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Post
            {
                Id = postId,
                UserId = userId,
                WorkspaceId = Guid.NewGuid(),
                Title = "Summer launch",
                Content = new PostContent
                {
                    Content = "New drop available now"
                }
            });

        userConfigService
            .Setup(service => service.GetActiveConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<UserAiConfig?>(new UserAiConfig(Guid.NewGuid(), "gpt-5-4", null, 4)));

        pricingService
            .Setup(service => service.GetCostAsync(
                CoinActionTypes.PostEnhancement,
                "gpt-5-4",
                null,
                1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new CoinCostQuote(
                CoinActionTypes.PostEnhancement,
                "gpt-5-4",
                null,
                "per_platform",
                3m,
                1,
                3m)));

        billingClient
            .Setup(client => client.DebitAsync(
                userId,
                3m,
                CoinDebitReasons.PostEnhancementDebit,
                CoinReferenceTypes.PostEnhancement,
                postId.ToString(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(40m));

        aiSpendRecordRepository
            .Setup(repository => repository.AddAsync(It.IsAny<AiSpendRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        aiSpendRecordRepository
            .Setup(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        geminiCaptionService
            .Setup(service => service.GenerateSocialMediaCaptionsAsync(
                It.IsAny<GeminiSocialMediaCaptionRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<GeminiGeneratedCaption>>(
            [
                new GeminiGeneratedCaption("Caption A", ["#a"], ["#trendA"], "CTA A"),
                new GeminiGeneratedCaption("Caption B", ["#b"], ["#trendB"], "CTA B")
            ]));

        var handler = new EnhanceExistingPostCommandHandler(
            postRepository.Object,
            userResourceService.Object,
            userConfigService.Object,
            geminiCaptionService.Object,
            pricingService.Object,
            billingClient.Object,
            aiSpendRecordRepository.Object);

        var result = await handler.Handle(
            new EnhanceExistingPostCommand(userId, postId, "instagram", null, "vi", "friendly", null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PostId.Should().Be(postId);
        result.Value.Platform.Should().Be("ig");
        result.Value.ResourceIds.Should().BeEmpty();
        result.Value.BestSuggestion.Caption.Should().Be("Caption A");
        result.Value.Alternatives.Should().ContainSingle();
        result.Value.Alternatives[0].Caption.Should().Be("Caption B");

        geminiCaptionService.Verify(service => service.GenerateSocialMediaCaptionsAsync(
            It.Is<GeminiSocialMediaCaptionRequest>(request =>
                request.Platform == "ig" &&
                request.Resources.Count == 0 &&
                request.CaptionCount == 3 &&
                request.LanguageHint == "Vietnamese" &&
                request.ResourceHints.Contains("Summer launch") &&
                request.ResourceHints.Contains("New drop available now")),
            It.IsAny<CancellationToken>()), Times.Once);

        userResourceService.VerifyNoOtherCalls();
        billingClient.Verify(client => client.RefundAsync(
            It.IsAny<Guid>(),
            It.IsAny<decimal>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ShouldUsePostResources_WhenRequestDoesNotOverrideThem()
    {
        var userId = Guid.NewGuid();
        var postId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();

        var handler = CreateHandler(
            userId,
            postId,
            new Post
            {
                Id = postId,
                UserId = userId,
                Content = new PostContent
                {
                    Content = "Polished studio shots",
                    ResourceList = [resourceId.ToString()]
                }
            },
            configuredResources: [new UserResourcePresignResult(resourceId, "https://cdn.example.com/a.jpg", "image/jpeg", "image")],
            generatedCaptions: [new GeminiGeneratedCaption("Caption", ["#a"], ["#trend"], "CTA")],
            out var userResourceService,
            out var geminiCaptionService,
            out _,
            out _);

        var result = await handler.Handle(
            new EnhanceExistingPostCommand(userId, postId, "facebook", null, null, null, 1),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ResourceIds.Should().Equal(resourceId);

        userResourceService.Verify(service => service.GetPresignedResourcesAsync(
            userId,
            It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == resourceId),
            It.IsAny<CancellationToken>()), Times.Once);

        geminiCaptionService.Verify(service => service.GenerateSocialMediaCaptionsAsync(
            It.Is<GeminiSocialMediaCaptionRequest>(request =>
                request.Platform == "facebook" &&
                request.Resources.Count == 1 &&
                request.Resources[0].FileUri == "https://cdn.example.com/a.jpg"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldPreferRequestOverrideResourceIds()
    {
        var userId = Guid.NewGuid();
        var postId = Guid.NewGuid();
        var postResourceId = Guid.NewGuid();
        var overrideResourceId = Guid.NewGuid();

        var handler = CreateHandler(
            userId,
            postId,
            new Post
            {
                Id = postId,
                UserId = userId,
                Content = new PostContent
                {
                    Content = "Styled product visuals",
                    ResourceList = [postResourceId.ToString()]
                }
            },
            configuredResources: [new UserResourcePresignResult(overrideResourceId, "https://cdn.example.com/override.jpg", "image/jpeg", "image")],
            generatedCaptions: [new GeminiGeneratedCaption("Caption", ["#a"], ["#trend"], "CTA")],
            out var userResourceService,
            out var geminiCaptionService,
            out _,
            out _);

        var result = await handler.Handle(
            new EnhanceExistingPostCommand(userId, postId, "threads", [overrideResourceId], null, null, 1),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ResourceIds.Should().Equal(overrideResourceId);

        userResourceService.Verify(service => service.GetPresignedResourcesAsync(
            userId,
            It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == overrideResourceId),
            It.IsAny<CancellationToken>()), Times.Once);

        geminiCaptionService.Verify(service => service.GenerateSocialMediaCaptionsAsync(
            It.Is<GeminiSocialMediaCaptionRequest>(request =>
                request.Platform == "threads" &&
                request.Resources.Count == 1 &&
                request.Resources[0].FileUri == "https://cdn.example.com/override.jpg"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldDebitBeforeCallingAi_AndRefundOnceWhenAiFails()
    {
        var userId = Guid.NewGuid();
        var postId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var callOrder = new List<string>();

        var postRepository = new Mock<IPostRepository>();
        var userResourceService = new Mock<IUserResourceService>();
        var userConfigService = new Mock<IUserConfigService>();
        var geminiCaptionService = new Mock<IGeminiCaptionService>();
        var pricingService = new Mock<ICoinPricingService>();
        var billingClient = new Mock<IBillingClient>();
        var aiSpendRecordRepository = new Mock<IAiSpendRecordRepository>();

        postRepository
            .Setup(repository => repository.GetByIdAsync(postId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Post
            {
                Id = postId,
                UserId = userId,
                Content = new PostContent { ResourceList = [resourceId.ToString()] }
            });

        userResourceService
            .Setup(service => service.GetPresignedResourcesAsync(userId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<UserResourcePresignResult>>(
            [
                new UserResourcePresignResult(resourceId, "https://cdn.example.com/r.jpg", "image/jpeg", "image")
            ]));

        userConfigService
            .Setup(service => service.GetActiveConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<UserAiConfig?>(new UserAiConfig(Guid.NewGuid(), "gpt-5-4", null, 3)));

        pricingService
            .Setup(service => service.GetCostAsync(CoinActionTypes.PostEnhancement, "gpt-5-4", null, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new CoinCostQuote(CoinActionTypes.PostEnhancement, "gpt-5-4", null, "per_platform", 3m, 1, 3m)));

        billingClient
            .Setup(client => client.DebitAsync(
                userId,
                3m,
                CoinDebitReasons.PostEnhancementDebit,
                CoinReferenceTypes.PostEnhancement,
                postId.ToString(),
                It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("debit"))
            .ReturnsAsync(Result.Success(20m));

        geminiCaptionService
            .Setup(service => service.GenerateSocialMediaCaptionsAsync(It.IsAny<GeminiSocialMediaCaptionRequest>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("ai"))
            .ReturnsAsync(Result.Failure<IReadOnlyList<GeminiGeneratedCaption>>(new Error("Gemini.Failed", "boom")));

        billingClient
            .Setup(client => client.RefundAsync(
                userId,
                3m,
                CoinDebitReasons.PostEnhancementRefund,
                CoinReferenceTypes.PostEnhancement,
                postId.ToString(),
                It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("refund"))
            .ReturnsAsync(Result.Success(23m));

        aiSpendRecordRepository
            .Setup(repository => repository.AddAsync(It.IsAny<AiSpendRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        aiSpendRecordRepository
            .Setup(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        aiSpendRecordRepository
            .Setup(repository => repository.Update(It.IsAny<AiSpendRecord>()));

        var handler = new EnhanceExistingPostCommandHandler(
            postRepository.Object,
            userResourceService.Object,
            userConfigService.Object,
            geminiCaptionService.Object,
            pricingService.Object,
            billingClient.Object,
            aiSpendRecordRepository.Object);

        var result = await handler.Handle(
            new EnhanceExistingPostCommand(userId, postId, "tiktok", null, null, null, 2),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Gemini.Failed");
        callOrder.Should().Equal("debit", "ai", "refund");
        billingClient.Verify(client => client.RefundAsync(
            userId,
            3m,
            CoinDebitReasons.PostEnhancementRefund,
            CoinReferenceTypes.PostEnhancement,
            postId.ToString(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldRejectOtherUsersPost()
    {
        var userId = Guid.NewGuid();
        var postId = Guid.NewGuid();

        var postRepository = new Mock<IPostRepository>();
        postRepository
            .Setup(repository => repository.GetByIdAsync(postId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Post
            {
                Id = postId,
                UserId = Guid.NewGuid()
            });

        var handler = new EnhanceExistingPostCommandHandler(
            postRepository.Object,
            Mock.Of<IUserResourceService>(),
            Mock.Of<IUserConfigService>(),
            Mock.Of<IGeminiCaptionService>(),
            Mock.Of<ICoinPricingService>(),
            Mock.Of<IBillingClient>(),
            Mock.Of<IAiSpendRecordRepository>());

        var result = await handler.Handle(
            new EnhanceExistingPostCommand(userId, postId, "instagram", null, null, null, null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(PostErrors.Unauthorized.Code);
    }

    [Fact]
    public async Task Handle_ShouldReturnEmptyAlternatives_WhenSingleSuggestionRequested()
    {
        var userId = Guid.NewGuid();
        var postId = Guid.NewGuid();

        var handler = CreateHandler(
            userId,
            postId,
            new Post
            {
                Id = postId,
                UserId = userId,
                Title = "Single suggestion",
                Content = new PostContent { Content = "Keep it simple" }
            },
            configuredResources: [],
            generatedCaptions: [new GeminiGeneratedCaption("Only one", ["#solo"], ["#trend"], "CTA")],
            out _,
            out _,
            out _,
            out _);

        var result = await handler.Handle(
            new EnhanceExistingPostCommand(userId, postId, "ig", null, null, null, 1),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.BestSuggestion.Should().NotBeNull();
        result.Value.Alternatives.Should().BeEmpty();
    }

    private static EnhanceExistingPostCommandHandler CreateHandler(
        Guid userId,
        Guid postId,
        Post post,
        IReadOnlyList<UserResourcePresignResult> configuredResources,
        IReadOnlyList<GeminiGeneratedCaption> generatedCaptions,
        out Mock<IUserResourceService> userResourceService,
        out Mock<IGeminiCaptionService> geminiCaptionService,
        out Mock<IBillingClient> billingClient,
        out Mock<IAiSpendRecordRepository> aiSpendRecordRepository)
    {
        var postRepository = new Mock<IPostRepository>();
        userResourceService = new Mock<IUserResourceService>();
        var userConfigService = new Mock<IUserConfigService>();
        geminiCaptionService = new Mock<IGeminiCaptionService>();
        var pricingService = new Mock<ICoinPricingService>();
        billingClient = new Mock<IBillingClient>();
        aiSpendRecordRepository = new Mock<IAiSpendRecordRepository>();

        postRepository
            .Setup(repository => repository.GetByIdAsync(postId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(post);

        userResourceService
            .Setup(service => service.GetPresignedResourcesAsync(userId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<UserResourcePresignResult>>(configuredResources));

        userConfigService
            .Setup(service => service.GetActiveConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<UserAiConfig?>(new UserAiConfig(Guid.NewGuid(), "gpt-5-4", null, 3)));

        pricingService
            .Setup(service => service.GetCostAsync(CoinActionTypes.PostEnhancement, "gpt-5-4", null, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new CoinCostQuote(CoinActionTypes.PostEnhancement, "gpt-5-4", null, "per_platform", 3m, 1, 3m)));

        billingClient
            .Setup(client => client.DebitAsync(
                userId,
                3m,
                CoinDebitReasons.PostEnhancementDebit,
                CoinReferenceTypes.PostEnhancement,
                postId.ToString(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(20m));

        aiSpendRecordRepository
            .Setup(repository => repository.AddAsync(It.IsAny<AiSpendRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        aiSpendRecordRepository
            .Setup(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        aiSpendRecordRepository
            .Setup(repository => repository.Update(It.IsAny<AiSpendRecord>()));

        geminiCaptionService
            .Setup(service => service.GenerateSocialMediaCaptionsAsync(It.IsAny<GeminiSocialMediaCaptionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(generatedCaptions));

        return new EnhanceExistingPostCommandHandler(
            postRepository.Object,
            userResourceService.Object,
            userConfigService.Object,
            geminiCaptionService.Object,
            pricingService.Object,
            billingClient.Object,
            aiSpendRecordRepository.Object);
    }
}
