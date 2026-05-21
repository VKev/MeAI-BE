using System.Security.Claims;
using Application.Abstractions.Billing;
using Application.Abstractions.Configs;
using Application.Billing;
using Application.Posts.Commands;
using Application.Posts.Models;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using SharedLibrary.Common.ResponseModel;
using WebApi.Controllers;

namespace test;

public sealed class AiGenerationControllerTests
{
    [Fact]
    public async Task EstimateCoin_ForCaptions_ReturnsQuoteAndBalance()
    {
        var userId = Guid.NewGuid();
        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        var pricingService = new Mock<ICoinPricingService>(MockBehavior.Strict);
        var billingClient = new Mock<IBillingClient>(MockBehavior.Strict);
        var userConfigService = new Mock<IUserConfigService>(MockBehavior.Strict);

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
            .Setup(client => client.GetBalanceAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(10m));

        var controller = CreateController(
            mediator.Object,
            pricingService.Object,
            billingClient.Object,
            userConfigService.Object,
            userId);

        var result = await controller.EstimateCoin(
            new AiGenerationEstimateRequest { Operation = "captions" },
            CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<Result<AiGenerationCoinEstimateResponse>>().Subject;
        payload.Value.Operation.Should().Be("captions");
        payload.Value.ActionType.Should().Be(CoinActionTypes.CaptionGeneration);
        payload.Value.TotalCoins.Should().Be(3m);
        payload.Value.CurrentBalance.Should().Be(10m);
        payload.Value.CanAfford.Should().BeTrue();
        payload.Value.ShortfallCoins.Should().Be(0m);
        pricingService.VerifyAll();
        billingClient.VerifyAll();
    }

    [Fact]
    public async Task EstimateCoin_ForPostPrepare_ReturnsZeroCostWithoutPricingLookup()
    {
        var userId = Guid.NewGuid();
        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        var pricingService = new Mock<ICoinPricingService>(MockBehavior.Strict);
        var billingClient = new Mock<IBillingClient>(MockBehavior.Strict);
        var userConfigService = new Mock<IUserConfigService>(MockBehavior.Strict);

        billingClient
            .Setup(client => client.GetBalanceAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(2m));

        var controller = CreateController(
            mediator.Object,
            pricingService.Object,
            billingClient.Object,
            userConfigService.Object,
            userId);

        var result = await controller.EstimateCoin(
            new AiGenerationEstimateRequest { Operation = "post-prepare" },
            CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<Result<AiGenerationCoinEstimateResponse>>().Subject;
        payload.Value.Operation.Should().Be("post_prepare");
        payload.Value.TotalCoins.Should().Be(0m);
        payload.Value.CurrentBalance.Should().Be(2m);
        payload.Value.CanAfford.Should().BeTrue();
        pricingService.Verify(
            service => service.GetCostAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        billingClient.VerifyAll();
    }

    [Fact]
    public async Task EstimateCoin_ForGeminiPost_UsesConfiguredBillingModel()
    {
        var userId = Guid.NewGuid();
        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        var pricingService = new Mock<ICoinPricingService>(MockBehavior.Strict);
        var billingClient = new Mock<IBillingClient>(MockBehavior.Strict);
        var userConfigService = new Mock<IUserConfigService>(MockBehavior.Strict);

        userConfigService
            .Setup(service => service.GetActiveConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<UserAiConfig?>(
                new UserAiConfig(Guid.NewGuid(), "gpt-5-2", null, null)));

        pricingService
            .Setup(service => service.GetCostAsync(
                CoinActionTypes.CaptionGeneration,
                "gpt-5-2",
                null,
                1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new CoinCostQuote(
                CoinActionTypes.CaptionGeneration,
                "gpt-5-2",
                null,
                "per_platform",
                2m,
                1,
                2m)));

        billingClient
            .Setup(client => client.GetBalanceAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(1m));

        var controller = CreateController(
            mediator.Object,
            pricingService.Object,
            billingClient.Object,
            userConfigService.Object,
            userId);

        var result = await controller.EstimateCoin(
            new AiGenerationEstimateRequest { Operation = "post" },
            CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<Result<AiGenerationCoinEstimateResponse>>().Subject;
        payload.Value.Operation.Should().Be("post");
        payload.Value.Model.Should().Be("gpt-5-2");
        payload.Value.TotalCoins.Should().Be(2m);
        payload.Value.CanAfford.Should().BeFalse();
        payload.Value.ShortfallCoins.Should().Be(1m);
        pricingService.VerifyAll();
        billingClient.VerifyAll();
        userConfigService.VerifyAll();
    }

    [Fact]
    public async Task GenerateSocialMediaCaptions_WhenCoinsAreInsufficient_ReturnsPaymentRequired()
    {
        var userId = Guid.NewGuid();
        var postId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var mediator = new Mock<IMediator>(MockBehavior.Strict);

        mediator
            .Setup(m => m.Send(
                It.Is<GenerateSocialMediaCaptionsCommand>(command =>
                    command.UserId == userId &&
                    command.SocialMedia.PostId == postId &&
                    command.SocialMedia.SocialMediaType == "tiktok" &&
                    command.SocialMedia.ResourceIds.Count == 1 &&
                    command.SocialMedia.ResourceIds[0] == resourceId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<GenerateSocialMediaCaptionsResponse>(
                new Error(BillingClientErrors.InsufficientFunds, "Insufficient MeAI coins.")));

        var controller = CreateController(
            mediator.Object,
            Mock.Of<ICoinPricingService>(),
            Mock.Of<IBillingClient>(),
            Mock.Of<IUserConfigService>(),
            userId);

        var result = await controller.GenerateSocialMediaCaptions(
            new GenerateSocialMediaCaptionsRequest
            {
                PostId = postId,
                Platform = "tiktok",
                ResourceIds = [resourceId]
            },
            CancellationToken.None);

        var paymentRequired = result.Should().BeOfType<ObjectResult>().Subject;
        paymentRequired.StatusCode.Should().Be(StatusCodes.Status402PaymentRequired);
        var problem = paymentRequired.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Type.Should().Be(BillingClientErrors.InsufficientFunds);
        mediator.VerifyAll();
    }

    [Fact]
    public async Task CreatePost_WhenCoinsAreInsufficient_ReturnsPaymentRequired()
    {
        var userId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var mediator = new Mock<IMediator>(MockBehavior.Strict);

        mediator
            .Setup(m => m.Send(
                It.Is<CreateGeminiPostCommand>(command =>
                    command.UserId == userId &&
                    command.ResourceIds.Count == 1 &&
                    command.ResourceIds[0] == resourceId &&
                    command.Caption == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<FacebookDraftPostResponse>(
                new Error(BillingClientErrors.InsufficientFunds, "Insufficient MeAI coins.")));

        var controller = CreateController(
            mediator.Object,
            Mock.Of<ICoinPricingService>(),
            Mock.Of<IBillingClient>(),
            Mock.Of<IUserConfigService>(),
            userId);

        var result = await controller.CreatePost(
            new GeminiPostRequest(null, [resourceId], null, "posts", null, null),
            CancellationToken.None);

        var paymentRequired = result.Should().BeOfType<ObjectResult>().Subject;
        paymentRequired.StatusCode.Should().Be(StatusCodes.Status402PaymentRequired);
        var problem = paymentRequired.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Type.Should().Be(BillingClientErrors.InsufficientFunds);
        mediator.VerifyAll();
    }

    private static AiGenerationController CreateController(
        IMediator mediator,
        ICoinPricingService pricingService,
        IBillingClient billingClient,
        IUserConfigService userConfigService,
        Guid userId)
    {
        return new AiGenerationController(mediator, pricingService, billingClient, userConfigService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) },
                        authenticationType: "test"))
                }
            }
        };
    }
}
