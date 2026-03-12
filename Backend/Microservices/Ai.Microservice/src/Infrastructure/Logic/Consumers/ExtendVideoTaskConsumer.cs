using Application.Abstractions;
using Domain.Entities;
using Infrastructure.Context;
using Infrastructure.Logic.Services;
using MassTransit;
using Microsoft.Extensions.Logging;
using SharedLibrary.Contracts.VideoGenerating;
using SharedLibrary.Extensions;
using System.Text.Json;

namespace Infrastructure.Logic.Consumers;

public class ExtendVideoTaskConsumer : IConsumer<VideoExtensionStarted>
{
    private readonly IVeoVideoService _veoVideoService;
    private readonly IAiFallbackTemplateService _fallbackTemplateService;
    private readonly MyDbContext _dbContext;
    private readonly ILogger<ExtendVideoTaskConsumer> _logger;

    public ExtendVideoTaskConsumer(
        IVeoVideoService veoVideoService,
        IAiFallbackTemplateService fallbackTemplateService,
        MyDbContext dbContext,
        ILogger<ExtendVideoTaskConsumer> logger)
    {
        _veoVideoService = veoVideoService;
        _fallbackTemplateService = fallbackTemplateService;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<VideoExtensionStarted> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Processing video extension request. CorrelationId: {CorrelationId}, OriginalTaskId: {OriginalTaskId}",
            message.CorrelationId,
            message.OriginalVeoTaskId);

        var videoTask = new VideoTask
        {
            Id = Guid.CreateVersion7(),
            UserId = message.UserId,
            CorrelationId = message.CorrelationId,
            Prompt = message.Prompt,
            Model = "extend",
            AspectRatio = "16:9",
            Status = "Submitted",
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        _dbContext.VideoTasks.Add(videoTask);
        await _dbContext.SaveChangesAsync(context.CancellationToken);

        var request = new VeoExtendRequest(
            TaskId: message.OriginalVeoTaskId,
            Prompt: message.Prompt,
            Seeds: message.Seeds,
            Watermark: message.Watermark,
            CorrelationId: message.CorrelationId);

        var result = await _veoVideoService.ExtendVideoAsync(request, context.CancellationToken);

        if (result.Success && !string.IsNullOrEmpty(result.TaskId))
        {
            videoTask.VeoTaskId = result.TaskId;
            videoTask.Status = "Processing";
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            _logger.LogInformation(
                "Video extension task created. CorrelationId: {CorrelationId}, VeoTaskId: {VeoTaskId}",
                message.CorrelationId,
                result.TaskId);

            await context.Publish(new VideoTaskCreated
            {
                CorrelationId = message.CorrelationId,
                VeoTaskId = result.TaskId,
                CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
            });
        }
        else
        {
            var completedAt = DateTimeExtensions.PostgreSqlUtcNow;
            if (_fallbackTemplateService.TryGetVideoFallback(out var fallbackAsset))
            {
                var fallbackTaskId = $"fallback-{message.CorrelationId:N}";
                var fallbackUrls = new List<string> { fallbackAsset.ResultUrl };
                var fallbackUrlsJson = JsonSerializer.Serialize(fallbackUrls);

                videoTask.VeoTaskId = fallbackTaskId;
                videoTask.Status = "Completed";
                videoTask.ResultUrls = fallbackUrlsJson;
                videoTask.Resolution = "fallback";
                videoTask.ErrorCode = null;
                videoTask.ErrorMessage = null;
                videoTask.CompletedAt = completedAt;
                await _dbContext.SaveChangesAsync(context.CancellationToken);

                _logger.LogWarning(
                    "Video extension submit failed; fallback template is used. CorrelationId: {CorrelationId}, Code: {Code}, Message: {Message}, FallbackUrl: {FallbackUrl}",
                    message.CorrelationId,
                    result.Code,
                    result.Message,
                    fallbackAsset.ResultUrl);

                await context.Publish(new VideoGenerationCompleted
                {
                    CorrelationId = message.CorrelationId,
                    VeoTaskId = fallbackTaskId,
                    ResultUrls = fallbackUrlsJson,
                    OriginUrls = null,
                    Resolution = "fallback",
                    CompletedAt = completedAt
                });

                return;
            }

            videoTask.Status = "Failed";
            videoTask.ErrorCode = result.Code;
            videoTask.ErrorMessage = result.Message;
            videoTask.CompletedAt = completedAt;
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            _logger.LogWarning(
                "Video extension failed. CorrelationId: {CorrelationId}, Code: {Code}, Message: {Message}",
                message.CorrelationId,
                result.Code,
                result.Message);

            await context.Publish(new VideoGenerationFailed
            {
                CorrelationId = message.CorrelationId,
                VeoTaskId = null,
                ErrorCode = result.Code,
                ErrorMessage = result.Message,
                FailedAt = DateTimeExtensions.PostgreSqlUtcNow
            });
        }
    }
}

