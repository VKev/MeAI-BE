using Application.Abstractions.Kie;
using Domain.Entities;
using Infrastructure.Context;
using MassTransit;
using Microsoft.Extensions.Logging;
using SharedLibrary.Contracts.ImageGenerating;
using SharedLibrary.Extensions;

namespace Infrastructure.Logic.Consumers;

public class SubmitImageTaskConsumer : IConsumer<ImageGenerationStarted>
{
    private readonly IKieImageService _kieImageService;
    private readonly MyDbContext _dbContext;
    private readonly ILogger<SubmitImageTaskConsumer> _logger;

    public SubmitImageTaskConsumer(
        IKieImageService kieImageService,
        MyDbContext dbContext,
        ILogger<SubmitImageTaskConsumer> logger)
    {
        _kieImageService = kieImageService;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ImageGenerationStarted> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Processing image generation request. CorrelationId: {CorrelationId}, Prompt: {Prompt}",
            message.CorrelationId,
            message.Prompt);

        var imageTask = new ImageTask
        {
            Id = Guid.CreateVersion7(),
            UserId = message.UserId,
            CorrelationId = message.CorrelationId,
            Prompt = message.Prompt,
            AspectRatio = message.AspectRatio,
            Resolution = message.Resolution,
            OutputFormat = message.OutputFormat,
            Status = "Submitted",
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        _dbContext.ImageTasks.Add(imageTask);
        await _dbContext.SaveChangesAsync(context.CancellationToken);

        var request = new KieGenerateRequest(
            Prompt: message.Prompt,
            ImageInput: message.ImageUrls,
            AspectRatio: message.AspectRatio,
            Resolution: message.Resolution,
            OutputFormat: message.OutputFormat,
            CorrelationId: message.CorrelationId);

        var result = await _kieImageService.GenerateImageAsync(request, context.CancellationToken);

        if (result.Success && !string.IsNullOrEmpty(result.TaskId))
        {
            imageTask.KieTaskId = result.TaskId;
            imageTask.Status = "Processing";
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            _logger.LogInformation(
                "Image task created. CorrelationId: {CorrelationId}, KieTaskId: {KieTaskId}",
                message.CorrelationId,
                result.TaskId);

            await context.Publish(new ImageTaskCreated
            {
                CorrelationId = message.CorrelationId,
                KieTaskId = result.TaskId,
                CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
            });
        }
        else
        {
            imageTask.Status = "Failed";
            imageTask.ErrorCode = result.Code;
            imageTask.ErrorMessage = result.Message;
            imageTask.CompletedAt = DateTimeExtensions.PostgreSqlUtcNow;
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            _logger.LogWarning(
                "Image generation failed. CorrelationId: {CorrelationId}, Code: {Code}, Message: {Message}",
                message.CorrelationId,
                result.Code,
                result.Message);

            await context.Publish(new ImageGenerationFailed
            {
                CorrelationId = message.CorrelationId,
                KieTaskId = null,
                ErrorCode = result.Code,
                ErrorMessage = result.Message,
                FailedAt = DateTimeExtensions.PostgreSqlUtcNow
            });
        }
    }
}
