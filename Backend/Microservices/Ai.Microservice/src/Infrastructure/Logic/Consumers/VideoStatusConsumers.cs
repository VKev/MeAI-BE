using Infrastructure.Context;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedLibrary.Contracts.VideoGenerating;

namespace Infrastructure.Logic.Consumers;

public class VideoCompletedConsumer : IConsumer<VideoGenerationCompleted>
{
    private readonly MyDbContext _dbContext;
    private readonly ILogger<VideoCompletedConsumer> _logger;

    public VideoCompletedConsumer(MyDbContext dbContext, ILogger<VideoCompletedConsumer> logger)
    {
        _dbContext = dbContext;
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
        }
        else
        {
            _logger.LogWarning(
                "VideoTask not found for CorrelationId: {CorrelationId}",
                message.CorrelationId);
        }
    }
}

public class VideoFailedConsumer : IConsumer<VideoGenerationFailed>
{
    private readonly MyDbContext _dbContext;
    private readonly ILogger<VideoFailedConsumer> _logger;

    public VideoFailedConsumer(MyDbContext dbContext, ILogger<VideoFailedConsumer> logger)
    {
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
        }
        else
        {
            _logger.LogWarning(
                "VideoTask not found for CorrelationId: {CorrelationId}",
                message.CorrelationId);
        }
    }
}

