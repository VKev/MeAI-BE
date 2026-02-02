using System.Text.Json;
using Application.Abstractions.Resources;
using Infrastructure.Context;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedLibrary.Contracts.ImageGenerating;
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
    private readonly MyDbContext _dbContext;
    private readonly ILogger<ImageFailedConsumer> _logger;

    public ImageFailedConsumer(MyDbContext dbContext, ILogger<ImageFailedConsumer> logger)
    {
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
        }
        else
        {
            _logger.LogWarning(
                "ImageTask not found for CorrelationId: {CorrelationId}",
                message.CorrelationId);
        }
    }
}
