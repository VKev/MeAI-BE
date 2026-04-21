using System.Text.Json;
using Application.Abstractions.Resources;
using Infrastructure.Context;
using Infrastructure.Logic.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedLibrary.Contracts.Notifications;
using SharedLibrary.Contracts.VideoGenerating;
using SharedLibrary.Extensions;

namespace Infrastructure.Logic.Consumers;

public class VideoCompletedConsumer : IConsumer<VideoGenerationCompleted>
{
    private readonly MyDbContext _dbContext;
    private readonly IUserResourceService _userResourceService;
    private readonly ILogger<VideoCompletedConsumer> _logger;

    public VideoCompletedConsumer(
        MyDbContext dbContext,
        IUserResourceService userResourceService,
        ILogger<VideoCompletedConsumer> logger)
    {
        _dbContext = dbContext;
        _userResourceService = userResourceService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<VideoGenerationCompleted> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Video generation completed. CorrelationId: {CorrelationId}, VeoTaskId: {VeoTaskId}",
            message.CorrelationId,
            message.VeoTaskId);

        var videoTask = await _dbContext.VideoTasks
            .FirstOrDefaultAsync(t => t.CorrelationId == message.CorrelationId, context.CancellationToken);

        if (videoTask is not null)
        {
            videoTask.Status = "Completed";
            videoTask.ResultUrls = message.ResultUrls;
            videoTask.Resolution = message.Resolution;
            videoTask.CompletedAt = message.CompletedAt;
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            _logger.LogInformation(
                "Video task updated to Completed. Id: {Id}, ResultUrls: {ResultUrls}",
                videoTask.Id,
                message.ResultUrls);

            List<string>? resultUrls = null;
            if (!string.IsNullOrWhiteSpace(message.ResultUrls))
            {
                try
                {
                    resultUrls = JsonSerializer.Deserialize<List<string>>(message.ResultUrls);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse result URLs for notification. CorrelationId: {CorrelationId}", message.CorrelationId);
                }
            }

            // Attach results to chat BEFORE publishing notification
            // so the FE refetch gets the updated data immediately
            await TryAttachChatResultsAsync(videoTask.UserId, videoTask.WorkspaceId, message.CorrelationId, message.ResultUrls, context.CancellationToken);

            await context.Publish(
                NotificationRequestedEventFactory.CreateForUser(
                    videoTask.UserId,
                    NotificationTypes.AiVideoGenerationCompleted,
                    "Video generation completed",
                    "Your generated video is ready.",
                    new
                    {
                        message.CorrelationId,
                        message.VeoTaskId,
                        resultCount = resultUrls?.Count ?? 0,
                        message.Resolution,
                        videoTask.Id,
                        videoTask.CompletedAt
                    },
                    createdAt: message.CompletedAt,
                    source: NotificationSourceConstants.Creator),
                context.CancellationToken);
        }
        else
        {
            _logger.LogWarning(
                "VideoTask not found for CorrelationId: {CorrelationId}",
                message.CorrelationId);
        }
    }

    private async Task TryAttachChatResultsAsync(
        Guid userId,
        Guid? workspaceId,
        Guid correlationId,
        string? resultUrlsJson,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(resultUrlsJson))
        {
            return;
        }

        List<string>? urls;
        try
        {
            urls = JsonSerializer.Deserialize<List<string>>(resultUrlsJson);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse ResultUrls for CorrelationId: {CorrelationId}", correlationId);
            return;
        }

        if (urls is null || urls.Count == 0)
        {
            return;
        }

        var uploadResult = await _userResourceService.CreateResourcesFromUrlsAsync(
            userId,
            urls,
            status: "generated",
            resourceType: "video",
            cancellationToken,
            workspaceId);

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
        chat.Status = "Completed";
        chat.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}

public class VideoFailedConsumer : IConsumer<VideoGenerationFailed>
{
    private readonly IAiFallbackTemplateService _fallbackTemplateService;
    private readonly MyDbContext _dbContext;
    private readonly ILogger<VideoFailedConsumer> _logger;

    public VideoFailedConsumer(
        IAiFallbackTemplateService fallbackTemplateService,
        MyDbContext dbContext,
        ILogger<VideoFailedConsumer> logger)
    {
        _fallbackTemplateService = fallbackTemplateService;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<VideoGenerationFailed> context)
    {
        var message = context.Message;

        _logger.LogWarning(
            "Video generation failed. CorrelationId: {CorrelationId}, Code: {Code}, Message: {Message}",
            message.CorrelationId,
            message.ErrorCode,
            message.ErrorMessage);

        if (_fallbackTemplateService.TryGetVideoFallback(out var fallbackAsset))
        {
            var completedAt = DateTimeExtensions.PostgreSqlUtcNow;
            var fallbackTaskId = string.IsNullOrWhiteSpace(message.VeoTaskId)
                ? $"fallback-{message.CorrelationId:N}"
                : message.VeoTaskId;
            var fallbackUrls = new List<string> { fallbackAsset.ResultUrl };

            _logger.LogWarning(
                "Video generation failed; fallback template is used. CorrelationId: {CorrelationId}, FallbackUrl: {FallbackUrl}",
                message.CorrelationId,
                fallbackAsset.ResultUrl);

            await context.Publish(new VideoGenerationCompleted
            {
                CorrelationId = message.CorrelationId,
                VeoTaskId = fallbackTaskId,
                ResultUrls = JsonSerializer.Serialize(fallbackUrls),
                OriginUrls = null,
                Resolution = "fallback",
                CompletedAt = completedAt
            });

            return;
        }

        var videoTask = await _dbContext.VideoTasks
            .FirstOrDefaultAsync(t => t.CorrelationId == message.CorrelationId, context.CancellationToken);

        if (videoTask is not null)
        {
            videoTask.Status = "Failed";
            videoTask.ErrorCode = message.ErrorCode;
            videoTask.ErrorMessage = message.ErrorMessage;
            videoTask.CompletedAt = message.FailedAt;
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            _logger.LogInformation(
                "Video task updated to Failed. Id: {Id}",
                videoTask.Id);

            await MarkChatAsFailedAsync(message.CorrelationId, message.ErrorMessage, context.CancellationToken);

            await context.Publish(
                NotificationRequestedEventFactory.CreateForUser(
                    videoTask.UserId,
                    NotificationTypes.AiVideoGenerationFailed,
                    "Video generation failed",
                    "Your video request could not be completed.",
                    new
                    {
                        message.CorrelationId,
                        message.VeoTaskId,
                        message.ErrorCode,
                        message.ErrorMessage,
                        videoTask.Id,
                        videoTask.CompletedAt
                    },
                    createdAt: message.FailedAt,
                    source: NotificationSourceConstants.Creator),
                context.CancellationToken);
        }
        else
        {
            _logger.LogWarning(
                "VideoTask not found for CorrelationId: {CorrelationId}",
                message.CorrelationId);
        }
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

        var chat = await _dbContext.Chats.FirstOrDefaultAsync(c => c.Id == matched.Id, cancellationToken);
        if (chat is null) return;

        chat.Status = "Failed";
        chat.ErrorMessage = errorMessage;
        chat.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}

