using System.Text.Json;
using Application.Abstractions.Billing;
using Application.Abstractions.Configs;
using Application.Abstractions.Resources;
using Application.Billing;
using Application.ChatSessions;
using Application.GenerationOptions;
using Domain.Entities;
using Domain.Repositories;
using MassTransit;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Contracts.VideoGenerating;
using SharedLibrary.Contracts.Notifications;
using SharedLibrary.Extensions;

namespace Application.Chats.Commands;

public sealed record CreateChatVideoCommand(
    Guid UserId,
    Guid ChatSessionId,
    string Prompt,
    IReadOnlyList<Guid> ResourceIds,
    string? Model,
    string? AspectRatio,
    int? Seeds,
    bool? EnableTranslation,
    string? Watermark,
    Guid? LinkedPostId = null,
    string? Variant = null,
    string? GenerationType = null,
    string? Resolution = null,
    int? Duration = null,
    bool? GenerateAudio = null,
    bool? ReturnLastFrame = null,
    bool? WebSearch = null) : IRequest<Result<ChatVideoResponse>>;

public sealed record ChatVideoResponse(
    Guid ChatId,
    Guid CorrelationId);

public sealed class CreateChatVideoCommandHandler
    : IRequestHandler<CreateChatVideoCommand, Result<ChatVideoResponse>>
{
    private readonly IChatRepository _chatRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IUserConfigService _userConfigService;
    private readonly IUserResourceService _userResourceService;
    private readonly IAiGenerationStorageEstimator _storageEstimator;
    private readonly ICoinPricingService _pricingService;
    private readonly IBillingClient _billingClient;
    private readonly IAiSpendRecordRepository _aiSpendRecordRepository;
    private readonly IBus _bus;

    public CreateChatVideoCommandHandler(
        IChatRepository chatRepository,
        IChatSessionRepository chatSessionRepository,
        IUserConfigService userConfigService,
        IUserResourceService userResourceService,
        IAiGenerationStorageEstimator storageEstimator,
        ICoinPricingService pricingService,
        IBillingClient billingClient,
        IAiSpendRecordRepository aiSpendRecordRepository,
        IBus bus)
    {
        _chatRepository = chatRepository;
        _chatSessionRepository = chatSessionRepository;
        _userConfigService = userConfigService;
        _userResourceService = userResourceService;
        _storageEstimator = storageEstimator;
        _pricingService = pricingService;
        _billingClient = billingClient;
        _aiSpendRecordRepository = aiSpendRecordRepository;
        _bus = bus;
    }

    public async Task<Result<ChatVideoResponse>> Handle(
        CreateChatVideoCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return Result.Failure<ChatVideoResponse>(new Error("Chat.InvalidPrompt", "Prompt is required."));
        }

        var session = await _chatSessionRepository.GetByIdAsync(request.ChatSessionId, cancellationToken);
        if (session is null)
        {
            return Result.Failure<ChatVideoResponse>(ChatSessionErrors.NotFound);
        }

        if (session.UserId != request.UserId)
        {
            return Result.Failure<ChatVideoResponse>(ChatSessionErrors.Unauthorized);
        }

        var resourceIds = request.ResourceIds?
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList() ?? new List<Guid>();

        var imageUrls = new List<string>();
        if (resourceIds.Count > 0)
        {
            var presignResult = await _userResourceService.GetPresignedResourcesAsync(
                request.UserId,
                resourceIds,
                cancellationToken);

            if (presignResult.IsFailure)
            {
                return Result.Failure<ChatVideoResponse>(presignResult.Error);
            }

            var presignedById = presignResult.Value.ToDictionary(r => r.ResourceId, r => r.PresignedUrl);
            imageUrls = resourceIds
                .Where(id => presignedById.TryGetValue(id, out _))
                .Select(id => presignedById[id])
                .ToList();
        }

        var activeConfig = await TryGetActiveConfigAsync(cancellationToken);
        var model = ResolveVideoModel(request.Model, activeConfig?.ChatModel);
        var variant = ResolveVideoVariant(model, request.Variant);
        var aspectRatio = ResolveValue(request.AspectRatio, activeConfig?.MediaAspectRatio, "16:9");
        var duration = VideoGenerationSettings.NormalizeDuration(model, request.Duration);
        var enableTranslation = request.EnableTranslation ?? true;
        var pricing = VideoPricingResolver.Resolve(model, variant, request.Resolution, duration);

        var correlationId = Guid.CreateVersion7();
        var estimatedBytes = _storageEstimator.EstimateVideoGenerationBytes(model);
        var quotaResult = await _userResourceService.CheckStorageQuotaAsync(
            request.UserId,
            estimatedBytes,
            "ai.generate.video",
            1,
            cancellationToken,
            session.WorkspaceId == Guid.Empty ? null : session.WorkspaceId);
        if (quotaResult.IsFailure)
        {
            return Result.Failure<ChatVideoResponse>(quotaResult.Error);
        }

        if (!quotaResult.Value.Allowed)
        {
            return Result.Failure<ChatVideoResponse>(BuildQuotaError(quotaResult.Value, estimatedBytes));
        }

        // Quote + charge coins BEFORE we persist the chat / enqueue the Kie Veo job.
        // Quote before enqueueing the provider job so insufficient balance reaches the
        // FE top-up modal without creating a chat or spend record.
        var videoQuoteResult = await _pricingService.GetCostAsync(
            CoinActionTypes.VideoGeneration,
            model,
            pricing.CatalogVariant,
            pricing.Quantity,
            cancellationToken);
        if (videoQuoteResult.IsFailure)
        {
            return Result.Failure<ChatVideoResponse>(videoQuoteResult.Error);
        }

        var chatId = Guid.CreateVersion7();
        var debitResult = await _billingClient.DebitAsync(
            request.UserId,
            videoQuoteResult.Value.TotalCoins,
            CoinDebitReasons.VideoGenerationDebit,
            CoinReferenceTypes.ChatVideo,
            chatId.ToString(),
            cancellationToken);
        if (debitResult.IsFailure)
        {
            return Result.Failure<ChatVideoResponse>(debitResult.Error);
        }

        var config = new ChatVideoConfig(
            correlationId,
            model,
            variant,
            aspectRatio,
            request.Seeds,
            enableTranslation,
            request.Watermark,
            request.LinkedPostId == Guid.Empty ? null : request.LinkedPostId,
            request.GenerationType,
            pricing.CatalogVariant,
            pricing.Quantity,
            request.Resolution,
            duration,
            request.GenerateAudio,
            request.ReturnLastFrame,
            request.WebSearch);

        var chat = new Chat
        {
            Id = chatId,
            SessionId = request.ChatSessionId,
            Prompt = request.Prompt.Trim(),
            Config = JsonSerializer.Serialize(config),
            ReferenceResourceIds = resourceIds.Count == 0 ? null : JsonSerializer.Serialize(resourceIds.Select(id => id.ToString())),
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        var workspaceId = session.WorkspaceId == Guid.Empty ? (Guid?)null : session.WorkspaceId;
        var messageCreatedAt = DateTimeExtensions.PostgreSqlUtcNow;

        await _chatRepository.AddAsync(chat, cancellationToken);
        await _aiSpendRecordRepository.AddAsync(
            new AiSpendRecord
            {
                Id = Guid.CreateVersion7(),
                UserId = request.UserId,
                WorkspaceId = workspaceId,
                Provider = AiSpendProviders.Kie,
                ActionType = CoinActionTypes.VideoGeneration,
                Model = model,
                Variant = pricing.CatalogVariant,
                Unit = videoQuoteResult.Value.Unit,
                Quantity = pricing.Quantity,
                UnitCostCoins = videoQuoteResult.Value.UnitCostCoins,
                TotalCoins = videoQuoteResult.Value.TotalCoins,
                ReferenceType = CoinReferenceTypes.ChatVideo,
                ReferenceId = chatId.ToString(),
                Status = AiSpendStatuses.Pending,
                CreatedAt = messageCreatedAt
            },
            cancellationToken);
        await _chatRepository.SaveChangesAsync(cancellationToken);

        var message = new VideoGenerationStarted
        {
            CorrelationId = correlationId,
            UserId = request.UserId,
            WorkspaceId = workspaceId,
            Prompt = chat.Prompt ?? string.Empty,
            ImageUrls = imageUrls,
            Model = model,
            Variant = variant,
            GenerationType = request.GenerationType,
            AspectRatio = aspectRatio,
            Seeds = request.Seeds,
            EnableTranslation = enableTranslation,
            Watermark = request.Watermark,
            Resolution = request.Resolution,
            Duration = duration,
            GenerateAudio = request.GenerateAudio,
            ReturnLastFrame = request.ReturnLastFrame,
            WebSearch = request.WebSearch,
            CreatedAt = messageCreatedAt
        };

        await _bus.Publish(message, cancellationToken);

        await _bus.Publish(
            NotificationRequestedEventFactory.CreateForUser(
                request.UserId,
                NotificationTypes.AiVideoGenerationSubmitted,
                "Video generation started",
                "Your video request was accepted and is being processed.",
                new
                {
                    correlationId,
                    chatId = chat.Id,
                    resourceIds,
                    model,
                    variant,
                    request.GenerationType,
                    aspectRatio,
                    request.Seeds,
                    enableTranslation,
                    request.Watermark,
                    request.Resolution,
                    duration,
                    request.GenerateAudio,
                    request.ReturnLastFrame,
                    request.WebSearch
                },
                request.UserId,
                message.CreatedAt,
                NotificationSourceConstants.Creator),
            cancellationToken);

        return Result.Success(new ChatVideoResponse(chat.Id, correlationId));
    }

    private sealed record ChatVideoConfig(
        Guid CorrelationId,
        string Model,
        string? Variant,
        string AspectRatio,
        int? Seeds,
        bool EnableTranslation,
        string? Watermark,
        Guid? LinkedPostId,
        string? GenerationType,
        string? BillingVariant,
        int BillingQuantity,
        string? Resolution,
        int? Duration,
        bool? GenerateAudio,
        bool? ReturnLastFrame,
        bool? WebSearch);

    private static Error BuildQuotaError(StorageQuotaCheckResult quota, long estimatedBytes)
    {
        var metadata = new Dictionary<string, object?>
        {
            ["quotaBytes"] = quota.QuotaBytes,
            ["usedBytes"] = quota.UsedBytes,
            ["reservedBytes"] = quota.ReservedBytes,
            ["requestedBytes"] = estimatedBytes,
            ["availableBytes"] = quota.AvailableBytes,
            ["estimatedBytes"] = estimatedBytes,
            ["estimatedFileCount"] = 1
        };

        if (string.Equals(quota.ErrorCode, "Resource.SystemStorageQuotaExceeded", StringComparison.Ordinal))
        {
            metadata["systemStorageQuotaBytes"] = quota.SystemStorageQuotaBytes;
        }

        return new Error(
            quota.ErrorCode ?? "Resource.StorageQuotaExceeded",
            string.IsNullOrWhiteSpace(quota.ErrorMessage) ? "Storage quota exceeded." : quota.ErrorMessage,
            metadata);
    }

    private async Task<UserAiConfig?> TryGetActiveConfigAsync(CancellationToken cancellationToken)
    {
        var result = await _userConfigService.GetActiveConfigAsync(cancellationToken);
        return result.IsSuccess ? result.Value : null;
    }

    private static string ResolveValue(string? requestedValue, string? configuredValue, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(requestedValue))
        {
            return requestedValue.Trim();
        }

        if (!string.IsNullOrWhiteSpace(configuredValue))
        {
            return configuredValue.Trim();
        }

        return fallback;
    }

    private static string ResolveVideoModel(string? requestedValue, string? configuredValue)
    {
        if (!string.IsNullOrWhiteSpace(requestedValue))
        {
            return requestedValue.Trim();
        }

        if (!string.IsNullOrWhiteSpace(configuredValue))
        {
            var normalizedConfiguredValue = configuredValue.Trim();
            if (ProviderGenerationModelCatalog.IsKnownModel("video", normalizedConfiguredValue))
            {
                return normalizedConfiguredValue;
            }
        }

        return "gemini-omni-video";
    }

    private static string? ResolveVideoVariant(string model, string? requestedValue)
    {
        if (!string.Equals(model, "veo-3-1", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return requestedValue?.Trim().ToLowerInvariant() switch
        {
            "lite" => "lite",
            "quality" => "quality",
            _ => "fast"
        };
    }
}
