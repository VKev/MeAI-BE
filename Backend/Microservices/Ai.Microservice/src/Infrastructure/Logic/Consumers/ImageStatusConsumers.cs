using System.Text.Json;
using Application.Abstractions.Resources;
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

        if (imageTask is not null)
        {
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

            await TryAttachChatResultsAsync(
                imageTask.UserId,
                message.CorrelationId,
                message.ResultUrls,
                context.CancellationToken);
        }
        else
        {
            _logger.LogWarning(
                "ImageTask not found for CorrelationId: {CorrelationId}",
                message.CorrelationId);
        }
    }

    private async Task TryAttachChatResultsAsync(
        Guid userId,
        Guid correlationId,
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
            cancellationToken);

        if (uploadResult.IsFailure)
        {
            _logger.LogWarning(
                "Failed to create resources for CorrelationId {CorrelationId}: {Error}",
                correlationId,
                uploadResult.Error.Description);
            return;
        }

        var resourceIds = uploadResult.Value.Select(resource => resource.ResourceId.ToString()).ToList();

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
            return;
        }

        var chat = await _dbContext.Chats
            .FirstOrDefaultAsync(c => c.Id == matched.Id, cancellationToken);

        if (chat is null)
        {
            _logger.LogWarning("Chat not found for CorrelationId: {CorrelationId}", correlationId);
            return;
        }

        chat.ResultResourceIds = JsonSerializer.Serialize(resourceIds);
        chat.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}

public class ImageFailedConsumer : IConsumer<ImageGenerationFailed>
{
    private readonly IKieFallbackCallbackService _kieFallbackCallbackService;
    private readonly MyDbContext _dbContext;
    private readonly ILogger<ImageFailedConsumer> _logger;

    public ImageFailedConsumer(
        IKieFallbackCallbackService kieFallbackCallbackService,
        MyDbContext dbContext,
        ILogger<ImageFailedConsumer> logger)
    {
        _kieFallbackCallbackService = kieFallbackCallbackService;
        _dbContext = dbContext;
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

        var isAlreadyFallbackTask = fallbackTaskId.StartsWith("fallback-", StringComparison.OrdinalIgnoreCase);
        if (!isAlreadyFallbackTask)
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

        var imageTask = await _dbContext.ImageTasks
            .FirstOrDefaultAsync(t => t.CorrelationId == message.CorrelationId, context.CancellationToken);

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
}
