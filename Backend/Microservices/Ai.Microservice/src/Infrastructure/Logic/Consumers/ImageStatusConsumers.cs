using System.Text.Json;
using Application.Abstractions.Billing;
using Application.Abstractions.Resources;
using Application.Billing;
using Domain.Entities;
using Infrastructure.Context;
using Infrastructure.Logic.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedLibrary.Contracts.ImageGenerating;
using SharedLibrary.Contracts.Notifications;
using SharedLibrary.Extensions;

namespace Infrastructure.Logic.Consumers;

public class ImageCompletedConsumer : IConsumer<ImageGenerationCompleted>
{
    private readonly MyDbContext _dbContext;
    private readonly IUserResourceService _userResourceService;
    private readonly ILogger<ImageCompletedConsumer> _logger;

    public ImageCompletedConsumer(
        MyDbContext dbContext,
        IUserResourceService userResourceService,
        ILogger<ImageCompletedConsumer> logger)
    {
        _dbContext = dbContext;
        _userResourceService = userResourceService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ImageGenerationCompleted> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Image generation completed. CorrelationId: {CorrelationId}, KieTaskId: {KieTaskId}",
            message.CorrelationId,
            message.KieTaskId);

        var imageTask = await _dbContext.ImageTasks
            .FirstOrDefaultAsync(t => t.CorrelationId == message.CorrelationId, context.CancellationToken);

        if (imageTask is null)
        {
            _logger.LogWarning(
                "ImageTask not found for CorrelationId: {CorrelationId}",
                message.CorrelationId);
            return;
        }

        imageTask.Status = "Completed";
        imageTask.ResultUrls = message.ResultUrls is not null
            ? JsonSerializer.Serialize(message.ResultUrls)
            : null;
        imageTask.CompletedAt = message.CompletedAt;
        await _dbContext.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation(
            "Image task updated to Completed. Id: {Id}, ResultUrls Count: {Count}",
            imageTask.Id,
            message.ResultUrls?.Count ?? 0);

        // Reframe tasks: append their single result to the parent chat.
        if (imageTask.ParentCorrelationId.HasValue)
        {
            await TryAppendChatResultsAsync(
                imageTask.UserId,
                imageTask.WorkspaceId,
                imageTask.ParentCorrelationId.Value,
                message.ResultUrls,
                context.CancellationToken);

            await context.Publish(
                NotificationRequestedEventFactory.CreateForUser(
                    imageTask.UserId,
                    NotificationTypes.AiImageGenerationCompleted,
                    "Reframed image ready",
                    "A resized variant is ready.",
                    new
                    {
                        parentCorrelationId = imageTask.ParentCorrelationId,
                        correlationId = message.CorrelationId,
                        message.KieTaskId,
                        resultCount = message.ResultUrls?.Count ?? 0,
                        imageTask.Id,
                        imageTask.CompletedAt
                    },
                    createdAt: message.CompletedAt,
                    source: NotificationSourceConstants.Creator),
                context.CancellationToken);
            return;
        }

        // Source image: upload to S3, attach IDs to the chat, and keep the presigned URLs
        // so reframes can use our own S3 URL (Kie temp URLs re-fed into Kie often fail).
        var uploaded = await TryAttachChatResultsAsync(
            imageTask.UserId,
            imageTask.WorkspaceId,
            message.CorrelationId,
            message.ResultUrls,
            context.CancellationToken);

        await context.Publish(
            NotificationRequestedEventFactory.CreateForUser(
                imageTask.UserId,
                NotificationTypes.AiImageGenerationCompleted,
                "Image generation completed",
                "Your generated image is ready.",
                new
                {
                    message.CorrelationId,
                    message.KieTaskId,
                    resultCount = message.ResultUrls?.Count ?? 0,
                    imageTask.Id,
                    imageTask.CompletedAt
                },
                createdAt: message.CompletedAt,
                source: NotificationSourceConstants.Creator),
            context.CancellationToken);

        if (!string.IsNullOrWhiteSpace(imageTask.SocialTargetsJson))
        {
            // Prefer our S3 presigned URL; fall back to Kie temp URL only if upload failed.
            var sourceUrl = uploaded.FirstOrDefault()?.PresignedUrl
                            ?? (message.ResultUrls is { Count: > 0 } ? message.ResultUrls[0] : null);

            if (!string.IsNullOrWhiteSpace(sourceUrl))
            {
                await FanOutReframesAsync(imageTask, sourceUrl, context);
            }
        }
    }

    private async Task FanOutReframesAsync(
        ImageTask sourceTask,
        string sourceImageUrl,
        ConsumeContext<ImageGenerationCompleted> context)
    {
        List<SocialTargetDto>? targets;
        try
        {
            targets = JsonSerializer.Deserialize<List<SocialTargetDto>>(sourceTask.SocialTargetsJson!);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to deserialize SocialTargets on ImageTask {Id}. CorrelationId: {CorrelationId}",
                sourceTask.Id,
                sourceTask.CorrelationId);
            return;
        }

        if (targets is null || targets.Count == 0)
        {
            return;
        }

        var sourceRatio = (sourceTask.AspectRatio ?? string.Empty).Trim();
        var dispatched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(sourceRatio))
        {
            // The source image already covers its own ratio — no reframe needed for it.
            dispatched.Add(sourceRatio);
        }

        foreach (var target in targets)
        {
            var ratio = target.Ratio?.Trim();
            if (string.IsNullOrWhiteSpace(ratio))
            {
                continue;
            }

            if (!dispatched.Add(ratio))
            {
                _logger.LogInformation(
                    "Skipping duplicate reframe for ratio {Ratio} (already covered). ParentCorrelationId: {Parent}",
                    ratio,
                    sourceTask.CorrelationId);
                continue;
            }

            var reframe = new ImageReframeRequested
            {
                CorrelationId = Guid.CreateVersion7(),
                ParentCorrelationId = sourceTask.CorrelationId,
                UserId = sourceTask.UserId,
                WorkspaceId = sourceTask.WorkspaceId,
                SourceImageUrl = sourceImageUrl,
                TargetRatio = ratio,
                SocialTarget = target,
                CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
            };

            await context.Publish(reframe, context.CancellationToken);

            _logger.LogInformation(
                "Published reframe request. ParentCorrelationId: {Parent}, TargetRatio: {Ratio}, Platform: {Platform}/{Type}",
                sourceTask.CorrelationId,
                ratio,
                target.Platform,
                target.Type);
        }
    }

    private async Task TryAppendChatResultsAsync(
        Guid userId,
        Guid? workspaceId,
        Guid parentCorrelationId,
        List<string>? resultUrls,
        CancellationToken cancellationToken)
    {
        if (resultUrls is null || resultUrls.Count == 0)
        {
            return;
        }

        var uploadResult = await _userResourceService.CreateResourcesFromUrlsAsync(
            userId,
            resultUrls,
            status: "generated",
            resourceType: "image",
            cancellationToken,
            workspaceId);

        if (uploadResult.IsFailure)
        {
            _logger.LogWarning(
                "Failed to create resources for reframe CorrelationId {CorrelationId}: {Error}",
                parentCorrelationId,
                uploadResult.Error.Description);
            return;
        }

        var newResourceIds = uploadResult.Value.Select(r => r.ResourceId.ToString()).ToList();

        var correlationText = parentCorrelationId.ToString();
        var candidates = await _dbContext.Chats
            .AsNoTracking()
            .Where(c => c.Config != null && !c.DeletedAt.HasValue)
            .ToListAsync(cancellationToken);

        var matched = candidates.FirstOrDefault(c =>
            c.Config != null &&
            c.Config.Contains(correlationText, StringComparison.OrdinalIgnoreCase));

        if (matched is null)
        {
            _logger.LogWarning(
                "Parent chat not found for reframe ParentCorrelationId: {CorrelationId}",
                parentCorrelationId);
            return;
        }

        var chat = await _dbContext.Chats
            .FirstOrDefaultAsync(c => c.Id == matched.Id, cancellationToken);

        if (chat is null)
        {
            return;
        }

        var existing = new List<string>();
        if (!string.IsNullOrWhiteSpace(chat.ResultResourceIds))
        {
            try
            {
                existing = JsonSerializer.Deserialize<List<string>>(chat.ResultResourceIds) ?? new List<string>();
            }
            catch (JsonException)
            {
                existing = new List<string>();
            }
        }

        foreach (var id in newResourceIds)
        {
            if (!existing.Contains(id))
            {
                existing.Add(id);
            }
        }

        chat.ResultResourceIds = JsonSerializer.Serialize(existing);
        chat.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<UserResourceCreatedResult>> TryAttachChatResultsAsync(
        Guid userId,
        Guid? workspaceId,
        Guid correlationId,
        List<string>? resultUrls,
        CancellationToken cancellationToken)
    {
        if (resultUrls is null || resultUrls.Count == 0)
        {
            return Array.Empty<UserResourceCreatedResult>();
        }

        var uploadResult = await _userResourceService.CreateResourcesFromUrlsAsync(
            userId,
            resultUrls,
            status: "generated",
            resourceType: "image",
            cancellationToken,
            workspaceId);

        if (uploadResult.IsFailure)
        {
            _logger.LogWarning(
                "Failed to create resources for CorrelationId {CorrelationId}: {Error}",
                correlationId,
                uploadResult.Error.Description);
            return Array.Empty<UserResourceCreatedResult>();
        }

        var uploaded = uploadResult.Value;
        var resourceIds = uploaded.Select(resource => resource.ResourceId.ToString()).ToList();

        var correlationText = correlationId.ToString();
        var candidates = await _dbContext.Chats
            .AsNoTracking()
            .Where(c => c.Config != null && !c.DeletedAt.HasValue)
            .ToListAsync(cancellationToken);

        var matched = candidates.FirstOrDefault(c =>
            c.Config != null &&
            c.Config.Contains(correlationText, StringComparison.OrdinalIgnoreCase));

        if (matched is null)
        {
            _logger.LogWarning("Chat not found for CorrelationId: {CorrelationId}", correlationId);
            return uploaded;
        }

        var chat = await _dbContext.Chats
            .FirstOrDefaultAsync(c => c.Id == matched.Id, cancellationToken);

        if (chat is null)
        {
            _logger.LogWarning("Chat not found for CorrelationId: {CorrelationId}", correlationId);
            return uploaded;
        }

        chat.ResultResourceIds = JsonSerializer.Serialize(resourceIds);
        chat.Status = "Completed";
        chat.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return uploaded;
    }
}

public class ImageFailedConsumer : IConsumer<ImageGenerationFailed>
{
    private readonly IKieFallbackCallbackService _kieFallbackCallbackService;
    private readonly MyDbContext _dbContext;
    private readonly IBillingClient _billingClient;
    private readonly ICoinPricingService _pricingService;
    private readonly ILogger<ImageFailedConsumer> _logger;

    public ImageFailedConsumer(
        IKieFallbackCallbackService kieFallbackCallbackService,
        MyDbContext dbContext,
        IBillingClient billingClient,
        ICoinPricingService pricingService,
        ILogger<ImageFailedConsumer> logger)
    {
        _kieFallbackCallbackService = kieFallbackCallbackService;
        _dbContext = dbContext;
        _billingClient = billingClient;
        _pricingService = pricingService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ImageGenerationFailed> context)
    {
        var message = context.Message;

        _logger.LogWarning(
            "Image generation failed. CorrelationId: {CorrelationId}, Code: {Code}, Message: {Message}",
            message.CorrelationId,
            message.ErrorCode,
            message.ErrorMessage);

        var fallbackTaskId = string.IsNullOrWhiteSpace(message.KieTaskId)
            ? $"fallback-{message.CorrelationId:N}"
            : message.KieTaskId;

        var imageTask = await _dbContext.ImageTasks
            .FirstOrDefaultAsync(t => t.CorrelationId == message.CorrelationId, context.CancellationToken);

        // Reframe tasks: do NOT fall back to a stock template — they have no prompt, a template
        // would make no sense. Just mark the reframe as failed; the parent chat keeps its source
        // image and the user sees one fewer variant.
        var isReframe = imageTask?.ParentCorrelationId.HasValue == true;

        var isAlreadyFallbackTask = fallbackTaskId.StartsWith("fallback-", StringComparison.OrdinalIgnoreCase);
        if (!isAlreadyFallbackTask && !isReframe)
        {
            var numberOfVariances = await ResolveNumberOfVariancesAsync(message.CorrelationId, context.CancellationToken);
            var callbackSent = await _kieFallbackCallbackService.SendImageSuccessCallbackAsync(
                message.CorrelationId,
                fallbackTaskId,
                numberOfVariances,
                context.CancellationToken);

            if (callbackSent)
            {
                _logger.LogWarning(
                    "Image generation failed from provider; fallback callback simulation was triggered. CorrelationId: {CorrelationId}",
                    message.CorrelationId);
                return;
            }
        }

        if (imageTask is not null)
        {
            imageTask.Status = "Failed";
            imageTask.ErrorCode = message.ErrorCode;
            imageTask.ErrorMessage = message.ErrorMessage;
            imageTask.CompletedAt = message.FailedAt;
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            _logger.LogInformation(
                "Image task updated to Failed. Id: {Id}",
                imageTask.Id);

            // For reframes: only publish a light "variant failed" notification so the FE
            // refetches and drops the pending skeleton. Do NOT mark the parent chat as Failed.
            if (isReframe)
            {
                await context.Publish(
                    NotificationRequestedEventFactory.CreateForUser(
                        imageTask.UserId,
                        NotificationTypes.AiImageGenerationFailed,
                        "Reframe variant failed",
                        "One of the resized variants could not be generated.",
                        new
                        {
                            parentCorrelationId = imageTask.ParentCorrelationId,
                            correlationId = message.CorrelationId,
                            message.KieTaskId,
                            message.ErrorCode,
                            message.ErrorMessage,
                            imageTask.Id,
                            imageTask.CompletedAt
                        },
                        createdAt: message.FailedAt,
                        source: NotificationSourceConstants.Creator),
                    context.CancellationToken);
                return;
            }

            await MarkChatAsFailedAsync(message.CorrelationId, message.ErrorMessage, context.CancellationToken);

            await context.Publish(
                NotificationRequestedEventFactory.CreateForUser(
                    imageTask.UserId,
                    NotificationTypes.AiImageGenerationFailed,
                    "Image generation failed",
                    "Your image request could not be completed.",
                    new
                    {
                        message.CorrelationId,
                        message.KieTaskId,
                        message.ErrorCode,
                        message.ErrorMessage,
                        imageTask.Id,
                        imageTask.CompletedAt
                    },
                    createdAt: message.FailedAt,
                    source: NotificationSourceConstants.Creator),
                context.CancellationToken);
        }
        else
        {
            _logger.LogWarning(
                "ImageTask not found for CorrelationId: {CorrelationId}",
                message.CorrelationId);
        }
    }

    private async Task<int> ResolveNumberOfVariancesAsync(Guid correlationId, CancellationToken cancellationToken)
    {
        var correlationText = correlationId.ToString();
        var candidates = await _dbContext.Chats
            .AsNoTracking()
            .Where(c => c.Config != null && !c.DeletedAt.HasValue)
            .ToListAsync(cancellationToken);

        var matched = candidates.FirstOrDefault(c =>
            c.Config != null &&
            c.Config.Contains(correlationText, StringComparison.OrdinalIgnoreCase));

        if (matched is null || string.IsNullOrWhiteSpace(matched.Config))
        {
            return 1;
        }

        try
        {
            var config = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(matched.Config);
            if (config is null)
            {
                return 1;
            }

            if (config.TryGetValue("NumberOfVariances", out var numberElement) ||
                config.TryGetValue("numberOfVariances", out numberElement))
            {
                if (numberElement.ValueKind == JsonValueKind.Number &&
                    numberElement.TryGetInt32(out var value) &&
                    value > 0)
                {
                    return value;
                }

                if (numberElement.ValueKind == JsonValueKind.String &&
                    int.TryParse(numberElement.GetString(), out var parsedValue) &&
                    parsedValue > 0)
                {
                    return parsedValue;
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to parse chat config while resolving image variance count. CorrelationId: {CorrelationId}",
                correlationId);
        }

        return 1;
    }

    private async Task MarkChatAsFailedAsync(Guid correlationId, string? errorMessage, CancellationToken cancellationToken)
    {
        var correlationText = correlationId.ToString();
        var candidates = await _dbContext.Chats
            .AsNoTracking()
            .Where(c => c.Config != null && !c.DeletedAt.HasValue)
            .ToListAsync(cancellationToken);

        var matched = candidates.FirstOrDefault(c =>
            c.Config != null &&
            c.Config.Contains(correlationText, StringComparison.OrdinalIgnoreCase));

        if (matched is null) return;

        var chat = await _dbContext.Chats
            .FirstOrDefaultAsync(c => c.Id == matched.Id, cancellationToken);
        if (chat is null) return;

        chat.Status = "Failed";
        chat.ErrorMessage = errorMessage;
        chat.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Look up the owning user via the chat session — Chat doesn't carry UserId directly.
        var userId = await _dbContext.ChatSessions
            .AsNoTracking()
            .Where(s => s.Id == chat.SessionId)
            .Select(s => (Guid?)s.UserId)
            .FirstOrDefaultAsync(cancellationToken);
        if (userId is null) return;

        // Full refund: re-quote the original charge from the chat's stored config so the
        // refund amount always matches the debit even if the catalog price has since been
        // edited by an admin. Idempotent via (reason, referenceId=chatId).
        await TryRefundChatAsync(chat, userId.Value, cancellationToken);
    }

    private async Task TryRefundChatAsync(Chat chat, Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            var (model, resolution, expectedResultCount) = ParseImageConfig(chat.Config);

            var quote = await _pricingService.GetCostAsync(
                CoinActionTypes.ImageGeneration,
                model,
                resolution,
                Math.Max(1, expectedResultCount),
                cancellationToken);
            if (quote.IsFailure)
            {
                _logger.LogWarning(
                    "Refund skipped for chat {ChatId}: pricing lookup failed ({Code}).",
                    chat.Id, quote.Error.Code);
                return;
            }

            var refund = await _billingClient.RefundAsync(
                userId,
                quote.Value.TotalCoins,
                CoinDebitReasons.ImageGenerationRefund,
                CoinReferenceTypes.ChatImage,
                chat.Id.ToString(),
                cancellationToken);
            if (refund.IsFailure)
            {
                _logger.LogWarning(
                    "Refund failed for chat {ChatId}: {Code} {Message}",
                    chat.Id, refund.Error.Code, refund.Error.Description);
                return;
            }

            await MarkSpendRecordsRefundedAsync(
                CoinReferenceTypes.ChatImage,
                chat.Id.ToString(),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error refunding chat {ChatId}", chat.Id);
        }
    }

    private async Task MarkSpendRecordsRefundedAsync(
        string referenceType,
        string referenceId,
        CancellationToken cancellationToken)
    {
        var records = await _dbContext.AiSpendRecords
            .Where(record => record.ReferenceType == referenceType && record.ReferenceId == referenceId)
            .ToListAsync(cancellationToken);

        if (records.Count == 0)
        {
            return;
        }

        var updatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        foreach (var record in records)
        {
            record.Status = AiSpendStatuses.Refunded;
            record.UpdatedAt = updatedAt;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static (string Model, string? Resolution, int ExpectedResultCount) ParseImageConfig(string? configJson)
    {
        // Defaults match CreateChatImageCommand's ResolveModel fallbacks.
        var model = "nano-banana-pro";
        string? resolution = "1K";
        var expected = 1;
        if (string.IsNullOrWhiteSpace(configJson)) return (model, resolution, expected);

        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(configJson);
            if (raw is null) return (model, resolution, expected);

            if (raw.TryGetValue("Model", out var modelEl) || raw.TryGetValue("model", out modelEl))
            {
                if (modelEl.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(modelEl.GetString()))
                {
                    model = modelEl.GetString()!.Trim();
                }
            }

            if (raw.TryGetValue("Resolution", out var resEl) || raw.TryGetValue("resolution", out resEl))
            {
                if (resEl.ValueKind == JsonValueKind.String)
                {
                    resolution = resEl.GetString();
                }
            }

            if (raw.TryGetValue("ExpectedResultCount", out var countEl) ||
                raw.TryGetValue("expectedResultCount", out countEl))
            {
                if (countEl.ValueKind == JsonValueKind.Number && countEl.TryGetInt32(out var n) && n > 0)
                {
                    expected = n;
                }
            }
        }
        catch (JsonException) { /* keep defaults */ }

        return (model, resolution, expected);
    }
}
