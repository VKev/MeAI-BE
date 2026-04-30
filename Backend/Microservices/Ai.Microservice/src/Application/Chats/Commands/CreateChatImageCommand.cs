using System.Text.Json;
using Application.Abstractions.Billing;
using Application.Abstractions.Configs;
using Application.Abstractions.Resources;
using Application.Billing;
using Application.ChatSessions;
using Domain.Entities;
using Domain.Repositories;
using MassTransit;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Contracts.ImageGenerating;
using SharedLibrary.Contracts.Notifications;
using SharedLibrary.Extensions;

namespace Application.Chats.Commands;

public sealed record CreateChatImageCommand(
    Guid UserId,
    Guid ChatSessionId,
    string Prompt,
    IReadOnlyList<Guid> ResourceIds,
    Guid? LinkedPostId,
    string? Model,
    string? AspectRatio,
    string? Resolution,
    string? OutputFormat,
    int? NumberOfVariances,
    IReadOnlyList<SocialTargetDto>? SocialTargets = null) : IRequest<Result<ChatImageResponse>>;

public sealed record ChatImageResponse(
    Guid ChatId,
    Guid CorrelationId);

public sealed class CreateChatImageCommandHandler
    : IRequestHandler<CreateChatImageCommand, Result<ChatImageResponse>>
{
    private readonly IChatRepository _chatRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IPostRepository _postRepository;
    private readonly IUserConfigService _userConfigService;
    private readonly IUserResourceService _userResourceService;
    private readonly ICoinPricingService _pricingService;
    private readonly IBillingClient _billingClient;
    private readonly IBus _bus;

    public CreateChatImageCommandHandler(
        IChatRepository chatRepository,
        IChatSessionRepository chatSessionRepository,
        IPostRepository postRepository,
        IUserConfigService userConfigService,
        IUserResourceService userResourceService,
        ICoinPricingService pricingService,
        IBillingClient billingClient,
        IBus bus)
    {
        _chatRepository = chatRepository;
        _chatSessionRepository = chatSessionRepository;
        _postRepository = postRepository;
        _userConfigService = userConfigService;
        _userResourceService = userResourceService;
        _pricingService = pricingService;
        _billingClient = billingClient;
        _bus = bus;
    }

    public async Task<Result<ChatImageResponse>> Handle(
        CreateChatImageCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return Result.Failure<ChatImageResponse>(new Error("Chat.InvalidPrompt", "Prompt is required."));
        }

        var session = await _chatSessionRepository.GetByIdAsync(request.ChatSessionId, cancellationToken);
        if (session is null)
        {
            return Result.Failure<ChatImageResponse>(ChatSessionErrors.NotFound);
        }

        if (session.UserId != request.UserId)
        {
            return Result.Failure<ChatImageResponse>(ChatSessionErrors.Unauthorized);
        }

        var linkedPostId = request.LinkedPostId == Guid.Empty ? null : request.LinkedPostId;
        if (linkedPostId.HasValue)
        {
            var linkedPost = await _postRepository.GetByIdAsync(linkedPostId.Value, cancellationToken);
            if (linkedPost is null || linkedPost.DeletedAt.HasValue)
            {
                return Result.Failure<ChatImageResponse>(new Error("Post.NotFound", "Linked post not found."));
            }

            if (linkedPost.UserId != request.UserId)
            {
                return Result.Failure<ChatImageResponse>(new Error("Post.Unauthorized", "You are not authorized to link this post."));
            }

            if (linkedPost.WorkspaceId.HasValue && linkedPost.WorkspaceId.Value != session.WorkspaceId)
            {
                return Result.Failure<ChatImageResponse>(new Error("Post.WorkspaceMismatch", "Linked post does not belong to the current chat session workspace."));
            }
        }

        var resourceIds = request.ResourceIds?
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList() ?? new List<Guid>();

        List<string> imageUrls = new();
        if (resourceIds.Count > 0)
        {
            var presignResult = await _userResourceService.GetPresignedResourcesAsync(
                request.UserId,
                resourceIds,
                cancellationToken);

            if (presignResult.IsFailure)
            {
                return Result.Failure<ChatImageResponse>(presignResult.Error);
            }

            var presignedById = presignResult.Value.ToDictionary(r => r.ResourceId, r => r.PresignedUrl);
            imageUrls = resourceIds
                .Where(id => presignedById.TryGetValue(id, out _))
                .Select(id => presignedById[id])
                .ToList();
        }

        var activeConfig = await TryGetActiveConfigAsync(cancellationToken);
        var model = ResolveModel(request.Model, activeConfig?.ChatModel, "nano-banana-pro");
        var aspectRatio = ResolveAspectRatio(request.AspectRatio, activeConfig?.MediaAspectRatio, "1:1");
        var resolution = string.IsNullOrWhiteSpace(request.Resolution) ? "1K" : request.Resolution.Trim();
        var outputFormat = string.IsNullOrWhiteSpace(request.OutputFormat) ? "png" : request.OutputFormat.Trim();
        var numberOfVariances = ResolveNumberOfVariances(request.NumberOfVariances, activeConfig?.NumberOfVariances, 1);

        var correlationId = Guid.CreateVersion7();

        var socialTargets = request.SocialTargets?
            .Where(t => !string.IsNullOrWhiteSpace(t.Platform) && !string.IsNullOrWhiteSpace(t.Ratio))
            .Select(t => new SocialTargetDto
            {
                Platform = t.Platform.Trim(),
                Type = string.IsNullOrWhiteSpace(t.Type) ? string.Empty : t.Type.Trim(),
                Ratio = t.Ratio.Trim()
            })
            .ToList();

        // When social targets are provided, the user's manual aspect ratio is irrelevant —
        // derive the source ratio from the most-frequent target ratio so the source image
        // can serve as-is for those targets and we only reframe for distinct remaining ratios.
        // Ties break by first occurrence.
        if (socialTargets is { Count: > 0 })
        {
            aspectRatio = socialTargets
                .GroupBy(t => t.Ratio, StringComparer.OrdinalIgnoreCase)
                .Select((g, index) => new { Ratio = g.Key, Count = g.Count(), FirstIndex = index })
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.FirstIndex)
                .First()
                .Ratio;
        }

        // Expected final tile count = source image + distinct reframe ratios (excluding source ratio).
        var expectedResultCount = 1;
        if (socialTargets is { Count: > 0 })
        {
            var distinctExtraRatios = socialTargets
                .Select(t => t.Ratio)
                .Where(r => !string.Equals(r, aspectRatio, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            expectedResultCount = 1 + distinctExtraRatios;
        }

        // Quote + charge coins BEFORE we persist the chat / enqueue the Kie job. Cost scales
        // with expectedResultCount (source image + distinct reframe ratios). If the user is
        // short we bubble Billing.InsufficientFunds up so the FE can pop the top-up modal.
        var quoteResult = await _pricingService.GetCostAsync(
            CoinActionTypes.ImageGeneration,
            model,
            resolution,
            Math.Max(1, expectedResultCount),
            cancellationToken);
        if (quoteResult.IsFailure)
        {
            return Result.Failure<ChatImageResponse>(quoteResult.Error);
        }

        var totalCost = quoteResult.Value.TotalCoins;
        var chatId = Guid.CreateVersion7();

        var debitResult = await _billingClient.DebitAsync(
            request.UserId,
            totalCost,
            CoinDebitReasons.ImageGenerationDebit,
            CoinReferenceTypes.ChatImage,
            chatId.ToString(),
            cancellationToken);
        if (debitResult.IsFailure)
        {
            return Result.Failure<ChatImageResponse>(debitResult.Error);
        }

        var config = new ChatImageConfig(
            correlationId,
            aspectRatio,
            resolution,
            outputFormat,
            numberOfVariances,
            expectedResultCount,
            linkedPostId,
            socialTargets is { Count: > 0 } ? socialTargets : null);

        var chat = new Chat
        {
            Id = chatId,
            SessionId = request.ChatSessionId,
            Prompt = request.Prompt.Trim(),
            Config = JsonSerializer.Serialize(config),
            ReferenceResourceIds = JsonSerializer.Serialize(resourceIds.Select(id => id.ToString())),
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _chatRepository.AddAsync(chat, cancellationToken);
        await _chatRepository.SaveChangesAsync(cancellationToken);

        var workspaceId = session.WorkspaceId == Guid.Empty ? (Guid?)null : session.WorkspaceId;

        var message = new ImageGenerationStarted
        {
            CorrelationId = correlationId,
            UserId = request.UserId,
            WorkspaceId = workspaceId,
            Prompt = chat.Prompt ?? string.Empty,
            ImageUrls = imageUrls,
            Model = model,
            AspectRatio = aspectRatio,
            Resolution = resolution,
            OutputFormat = outputFormat,
            NumberOfVariances = numberOfVariances,
            SocialTargets = socialTargets is { Count: > 0 } ? socialTargets : null,
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _bus.Publish(message, cancellationToken);

        await _bus.Publish(
            NotificationRequestedEventFactory.CreateForUser(
                request.UserId,
                NotificationTypes.AiImageGenerationSubmitted,
                "Image generation started",
                "Your image request was accepted and is being processed.",
                new
                {
                    correlationId,
                    chatId = chat.Id,
                    resourceIds,
                    aspectRatio,
                    resolution,
                    outputFormat,
                    numberOfVariances
                },
                request.UserId,
                message.CreatedAt,
                NotificationSourceConstants.Creator),
            cancellationToken);

        return Result.Success(new ChatImageResponse(chat.Id, correlationId));
    }

    private sealed record ChatImageConfig(
        Guid CorrelationId,
        string AspectRatio,
        string Resolution,
        string OutputFormat,
        int NumberOfVariances,
        int ExpectedResultCount,
        Guid? LinkedPostId,
        List<SocialTargetDto>? SocialTargets);

    private async Task<UserAiConfig?> TryGetActiveConfigAsync(CancellationToken cancellationToken)
    {
        var result = await _userConfigService.GetActiveConfigAsync(cancellationToken);
        return result.IsSuccess ? result.Value : null;
    }

    private static int ResolveNumberOfVariances(int? requestedValue, int? configuredValue, int fallback)
    {
        if (requestedValue.GetValueOrDefault() > 0)
        {
            return requestedValue.Value;
        }

        if (configuredValue.GetValueOrDefault() > 0)
        {
            return configuredValue.Value;
        }

        return fallback;
    }

    private static string ResolveModel(string? requestedValue, string? configuredValue, string fallback)
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

    private static string ResolveAspectRatio(string? requestedValue, string? configuredValue, string fallback)
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
}
