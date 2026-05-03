using Application.Abstractions.Billing;
using Application.Abstractions.Configs;
using Application.Abstractions.Resources;
using Application.Billing;
using Application.Chats.Commands;
using Domain.Entities;
using Domain.Repositories;
using FluentAssertions;
using MassTransit;
using Moq;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Contracts.ImageGenerating;
using SharedLibrary.Contracts.Notifications;
using SharedLibrary.Contracts.VideoGenerating;

namespace AiMicroservice.Tests.Application.Chats.Commands;

public sealed class CreateChatGenerationQuotaCommandTests
{
    [Fact]
    public async Task CreateChatImage_ShouldContinueFlow_WhenQuotaAllowsGeneration()
    {
        var userId = Guid.NewGuid();
        var chatSessionId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();

        var chatRepository = new Mock<IChatRepository>();
        var chatSessionRepository = new Mock<IChatSessionRepository>();
        var postRepository = new Mock<IPostRepository>();
        var userConfigService = new Mock<IUserConfigService>();
        var userResourceService = new Mock<IUserResourceService>();
        var storageEstimator = new Mock<IAiGenerationStorageEstimator>();
        var pricingService = new Mock<ICoinPricingService>();
        var billingClient = new Mock<IBillingClient>();
        var aiSpendRecordRepository = new Mock<IAiSpendRecordRepository>();
        var bus = new Mock<IBus>();

        chatSessionRepository
            .Setup(repository => repository.GetByIdAsync(chatSessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatSession
            {
                Id = chatSessionId,
                UserId = userId,
                WorkspaceId = workspaceId
            });

        userConfigService
            .Setup(service => service.GetActiveConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<UserAiConfig?>(null));

        storageEstimator
            .Setup(estimator => estimator.EstimateImageGenerationBytes("nano-banana-pro", "1K", 2))
            .Returns(10L * 1024 * 1024);

        userResourceService
            .Setup(service => service.CheckStorageQuotaAsync(
                userId,
                10L * 1024 * 1024,
                "ai.generate.image",
                2,
                It.IsAny<CancellationToken>(),
                workspaceId))
            .ReturnsAsync(Result.Success(new StorageQuotaCheckResult(
                true,
                100L * 1024 * 1024,
                20L * 1024 * 1024,
                0,
                80L * 1024 * 1024,
                null,
                null,
                null,
                null)));

        pricingService
            .Setup(service => service.GetCostAsync(
                CoinActionTypes.ImageGeneration,
                "nano-banana-pro",
                "1K",
                2,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new CoinCostQuote(
                CoinActionTypes.ImageGeneration,
                "nano-banana-pro",
                "1K",
                "per_image",
                5m,
                2,
                10m)));

        billingClient
            .Setup(client => client.DebitAsync(
                userId,
                10m,
                CoinDebitReasons.ImageGenerationDebit,
                CoinReferenceTypes.ChatImage,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(90m));

        chatRepository
            .Setup(repository => repository.AddAsync(It.IsAny<Chat>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        chatRepository
            .Setup(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        aiSpendRecordRepository
            .Setup(repository => repository.AddRangeAsync(It.IsAny<IEnumerable<AiSpendRecord>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        bus
            .Setup(service => service.Publish(It.IsAny<ImageGenerationStarted>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        bus
            .Setup(service => service.Publish(It.IsAny<NotificationRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new CreateChatImageCommandHandler(
            chatRepository.Object,
            chatSessionRepository.Object,
            postRepository.Object,
            userConfigService.Object,
            userResourceService.Object,
            storageEstimator.Object,
            pricingService.Object,
            billingClient.Object,
            aiSpendRecordRepository.Object,
            bus.Object);

        var result = await handler.Handle(
            new CreateChatImageCommand(
                userId,
                chatSessionId,
                "Generate launch visuals",
                [],
                null,
                null,
                null,
                null,
                null,
                null,
                [
                    new SocialTargetDto { Platform = "instagram", Ratio = "1:1", Type = "feed" },
                    new SocialTargetDto { Platform = "facebook", Ratio = "16:9", Type = "story" }
                ]),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ChatId.Should().NotBeEmpty();
        result.Value.CorrelationId.Should().NotBeEmpty();

        billingClient.Verify(client => client.DebitAsync(
            userId,
            10m,
            CoinDebitReasons.ImageGenerationDebit,
            CoinReferenceTypes.ChatImage,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
        bus.Verify(service => service.Publish(It.IsAny<ImageGenerationStarted>(), It.IsAny<CancellationToken>()), Times.Once);
        bus.Verify(service => service.Publish(It.IsAny<NotificationRequestedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        chatRepository.Verify(repository => repository.AddAsync(It.IsAny<Chat>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateChatImage_ShouldFailBeforeDebit_WhenQuotaExceeded()
    {
        var userId = Guid.NewGuid();
        var chatSessionId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();

        var chatRepository = new Mock<IChatRepository>(MockBehavior.Strict);
        var chatSessionRepository = new Mock<IChatSessionRepository>();
        var postRepository = new Mock<IPostRepository>(MockBehavior.Strict);
        var userConfigService = new Mock<IUserConfigService>();
        var userResourceService = new Mock<IUserResourceService>();
        var storageEstimator = new Mock<IAiGenerationStorageEstimator>();
        var pricingService = new Mock<ICoinPricingService>(MockBehavior.Strict);
        var billingClient = new Mock<IBillingClient>(MockBehavior.Strict);
        var aiSpendRecordRepository = new Mock<IAiSpendRecordRepository>(MockBehavior.Strict);
        var bus = new Mock<IBus>(MockBehavior.Strict);

        chatSessionRepository
            .Setup(repository => repository.GetByIdAsync(chatSessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatSession
            {
                Id = chatSessionId,
                UserId = userId,
                WorkspaceId = workspaceId
            });

        userConfigService
            .Setup(service => service.GetActiveConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<UserAiConfig?>(null));

        storageEstimator
            .Setup(estimator => estimator.EstimateImageGenerationBytes("nano-banana-pro", "1K", 3))
            .Returns(15L * 1024 * 1024);

        userResourceService
            .Setup(service => service.CheckStorageQuotaAsync(
                userId,
                15L * 1024 * 1024,
                "ai.generate.image",
                3,
                It.IsAny<CancellationToken>(),
                workspaceId))
            .ReturnsAsync(Result.Success(new StorageQuotaCheckResult(
                false,
                12L * 1024 * 1024,
                10L * 1024 * 1024,
                0,
                2L * 1024 * 1024,
                null,
                null,
                "Resource.StorageQuotaExceeded",
                "Storage quota exceeded.")));

        var handler = new CreateChatImageCommandHandler(
            chatRepository.Object,
            chatSessionRepository.Object,
            postRepository.Object,
            userConfigService.Object,
            userResourceService.Object,
            storageEstimator.Object,
            pricingService.Object,
            billingClient.Object,
            aiSpendRecordRepository.Object,
            bus.Object);

        var result = await handler.Handle(
            new CreateChatImageCommand(
                userId,
                chatSessionId,
                "Generate multiple crops",
                [],
                null,
                null,
                null,
                null,
                null,
                null,
                [
                    new SocialTargetDto { Platform = "instagram", Ratio = "1:1", Type = "feed" },
                    new SocialTargetDto { Platform = "facebook", Ratio = "16:9", Type = "story" },
                    new SocialTargetDto { Platform = "linkedin", Ratio = "4:5", Type = "post" }
                ]),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Resource.StorageQuotaExceeded");
        result.Error.Metadata.Should().NotBeNull();
        result.Error.Metadata!["requestedBytes"].Should().Be(15L * 1024 * 1024);
        result.Error.Metadata["estimatedBytes"].Should().Be(15L * 1024 * 1024);
        result.Error.Metadata["estimatedFileCount"].Should().Be(3);

        pricingService.VerifyNoOtherCalls();
        billingClient.VerifyNoOtherCalls();
        chatRepository.VerifyNoOtherCalls();
        aiSpendRecordRepository.VerifyNoOtherCalls();
        bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateChatVideo_ShouldContinueFlow_WhenQuotaAllowsGeneration()
    {
        var userId = Guid.NewGuid();
        var chatSessionId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();

        var chatRepository = new Mock<IChatRepository>();
        var chatSessionRepository = new Mock<IChatSessionRepository>();
        var userConfigService = new Mock<IUserConfigService>();
        var userResourceService = new Mock<IUserResourceService>();
        var storageEstimator = new Mock<IAiGenerationStorageEstimator>();
        var pricingService = new Mock<ICoinPricingService>();
        var billingClient = new Mock<IBillingClient>();
        var aiSpendRecordRepository = new Mock<IAiSpendRecordRepository>();
        var bus = new Mock<IBus>();

        chatSessionRepository
            .Setup(repository => repository.GetByIdAsync(chatSessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatSession
            {
                Id = chatSessionId,
                UserId = userId,
                WorkspaceId = workspaceId
            });

        userConfigService
            .Setup(service => service.GetActiveConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<UserAiConfig?>(null));

        storageEstimator
            .Setup(estimator => estimator.EstimateVideoGenerationBytes("veo3_fast"))
            .Returns(150L * 1024 * 1024);

        userResourceService
            .Setup(service => service.CheckStorageQuotaAsync(
                userId,
                150L * 1024 * 1024,
                "ai.generate.video",
                1,
                It.IsAny<CancellationToken>(),
                workspaceId))
            .ReturnsAsync(Result.Success(new StorageQuotaCheckResult(
                true,
                500L * 1024 * 1024,
                50L * 1024 * 1024,
                0,
                450L * 1024 * 1024,
                null,
                null,
                null,
                null)));

        pricingService
            .Setup(service => service.GetCostAsync(
                CoinActionTypes.VideoGeneration,
                "veo3_fast",
                null,
                1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new CoinCostQuote(
                CoinActionTypes.VideoGeneration,
                "veo3_fast",
                null,
                "per_video",
                90m,
                1,
                90m)));

        billingClient
            .Setup(client => client.DebitAsync(
                userId,
                90m,
                CoinDebitReasons.VideoGenerationDebit,
                CoinReferenceTypes.ChatVideo,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(10m));

        chatRepository
            .Setup(repository => repository.AddAsync(It.IsAny<Chat>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        chatRepository
            .Setup(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        aiSpendRecordRepository
            .Setup(repository => repository.AddAsync(It.IsAny<AiSpendRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        bus
            .Setup(service => service.Publish(It.IsAny<VideoGenerationStarted>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        bus
            .Setup(service => service.Publish(It.IsAny<NotificationRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new CreateChatVideoCommandHandler(
            chatRepository.Object,
            chatSessionRepository.Object,
            userConfigService.Object,
            userResourceService.Object,
            storageEstimator.Object,
            pricingService.Object,
            billingClient.Object,
            aiSpendRecordRepository.Object,
            bus.Object);

        var result = await handler.Handle(
            new CreateChatVideoCommand(
                userId,
                chatSessionId,
                "Animate a product teaser",
                [],
                null,
                null,
                42,
                true,
                "false"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        billingClient.Verify(client => client.DebitAsync(
            userId,
            90m,
            CoinDebitReasons.VideoGenerationDebit,
            CoinReferenceTypes.ChatVideo,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
        bus.Verify(service => service.Publish(It.IsAny<VideoGenerationStarted>(), It.IsAny<CancellationToken>()), Times.Once);
        bus.Verify(service => service.Publish(It.IsAny<NotificationRequestedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateChatVideo_ShouldFailBeforeDebit_WhenSystemQuotaExceeded()
    {
        var userId = Guid.NewGuid();
        var chatSessionId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();

        var chatRepository = new Mock<IChatRepository>(MockBehavior.Strict);
        var chatSessionRepository = new Mock<IChatSessionRepository>();
        var userConfigService = new Mock<IUserConfigService>();
        var userResourceService = new Mock<IUserResourceService>();
        var storageEstimator = new Mock<IAiGenerationStorageEstimator>();
        var pricingService = new Mock<ICoinPricingService>(MockBehavior.Strict);
        var billingClient = new Mock<IBillingClient>(MockBehavior.Strict);
        var aiSpendRecordRepository = new Mock<IAiSpendRecordRepository>(MockBehavior.Strict);
        var bus = new Mock<IBus>(MockBehavior.Strict);

        chatSessionRepository
            .Setup(repository => repository.GetByIdAsync(chatSessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatSession
            {
                Id = chatSessionId,
                UserId = userId,
                WorkspaceId = workspaceId
            });

        userConfigService
            .Setup(service => service.GetActiveConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<UserAiConfig?>(null));

        storageEstimator
            .Setup(estimator => estimator.EstimateVideoGenerationBytes("veo3_fast"))
            .Returns(150L * 1024 * 1024);

        userResourceService
            .Setup(service => service.CheckStorageQuotaAsync(
                userId,
                150L * 1024 * 1024,
                "ai.generate.video",
                1,
                It.IsAny<CancellationToken>(),
                workspaceId))
            .ReturnsAsync(Result.Success(new StorageQuotaCheckResult(
                false,
                500L * 1024 * 1024,
                480L * 1024 * 1024,
                0,
                20L * 1024 * 1024,
                null,
                600L * 1024 * 1024,
                "Resource.SystemStorageQuotaExceeded",
                "System storage quota exceeded.")));

        var handler = new CreateChatVideoCommandHandler(
            chatRepository.Object,
            chatSessionRepository.Object,
            userConfigService.Object,
            userResourceService.Object,
            storageEstimator.Object,
            pricingService.Object,
            billingClient.Object,
            aiSpendRecordRepository.Object,
            bus.Object);

        var result = await handler.Handle(
            new CreateChatVideoCommand(
                userId,
                chatSessionId,
                "Create a cinematic reveal",
                [],
                null,
                null,
                null,
                null,
                null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Resource.SystemStorageQuotaExceeded");
        result.Error.Metadata.Should().NotBeNull();
        result.Error.Metadata!["estimatedBytes"].Should().Be(150L * 1024 * 1024);
        result.Error.Metadata["estimatedFileCount"].Should().Be(1);
        result.Error.Metadata["systemStorageQuotaBytes"].Should().Be(600L * 1024 * 1024);

        pricingService.VerifyNoOtherCalls();
        billingClient.VerifyNoOtherCalls();
        chatRepository.VerifyNoOtherCalls();
        aiSpendRecordRepository.VerifyNoOtherCalls();
        bus.VerifyNoOtherCalls();
    }
}
