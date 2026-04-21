using Application.Abstractions.Kie;
using Domain.Entities;
using Infrastructure.Context;
using MassTransit;
using Microsoft.Extensions.Logging;
using SharedLibrary.Contracts.ImageGenerating;
using SharedLibrary.Extensions;

namespace Infrastructure.Logic.Consumers;

public class SubmitImageReframeConsumer : IConsumer<ImageReframeRequested>
{
    private const string ReframeModel = "flux-kontext-pro";
    private const string ReframePrompt = "Keep the exact same subject and composition; only adjust the framing to match the new aspect ratio.";

    private readonly IKieImageService _kieImageService;
    private readonly MyDbContext _dbContext;
    private readonly ILogger<SubmitImageReframeConsumer> _logger;

    public SubmitImageReframeConsumer(
        IKieImageService kieImageService,
        MyDbContext dbContext,
        ILogger<SubmitImageReframeConsumer> logger)
    {
        _kieImageService = kieImageService;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ImageReframeRequested> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Processing image reframe. CorrelationId: {CorrelationId}, ParentCorrelationId: {ParentCorrelationId}, TargetRatio: {TargetRatio}",
            message.CorrelationId,
            message.ParentCorrelationId,
            message.TargetRatio);

        var imageTask = new ImageTask
        {
            Id = Guid.CreateVersion7(),
            UserId = message.UserId,
            WorkspaceId = message.WorkspaceId,
            CorrelationId = message.CorrelationId,
            ParentCorrelationId = message.ParentCorrelationId,
            Prompt = $"[reframe] {message.TargetRatio}",
            AspectRatio = message.TargetRatio,
            Resolution = "1K",
            OutputFormat = "png",
            Status = "Submitted",
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        _dbContext.ImageTasks.Add(imageTask);
        await _dbContext.SaveChangesAsync(context.CancellationToken);

        var request = new KieGenerateRequest(
            Prompt: ReframePrompt,
            ImageInput: new List<string> { message.SourceImageUrl },
            Model: ReframeModel,
            AspectRatio: message.TargetRatio,
            Resolution: "1K",
            OutputFormat: "png",
            NumberOfVariances: 1,
            CorrelationId: message.CorrelationId);

        // Kie ideogram/v3-reframe occasionally returns transient 5xx ("internal error, please try again later").
        // Retry up to 3 times with short backoff before giving up.
        const int maxAttempts = 3;
        KieGenerateResult result = default!;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            result = await _kieImageService.GenerateImageAsync(request, context.CancellationToken);

            if (result.Success && !string.IsNullOrEmpty(result.TaskId))
            {
                break;
            }

            var isTransient = result.Code >= 500;
            if (!isTransient || attempt == maxAttempts)
            {
                break;
            }

            _logger.LogWarning(
                "Reframe submit returned {Code} (attempt {Attempt}/{Max}); retrying. CorrelationId: {CorrelationId}",
                result.Code,
                attempt,
                maxAttempts,
                message.CorrelationId);

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), context.CancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        if (result.Success && !string.IsNullOrEmpty(result.TaskId))
        {
            imageTask.KieTaskId = result.TaskId;
            imageTask.Status = "Processing";
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            _logger.LogInformation(
                "Reframe task created. CorrelationId: {CorrelationId}, KieTaskId: {KieTaskId}",
                message.CorrelationId,
                result.TaskId);
        }
        else
        {
            imageTask.Status = "Failed";
            imageTask.ErrorCode = result.Code;
            imageTask.ErrorMessage = result.Message;
            imageTask.CompletedAt = DateTimeExtensions.PostgreSqlUtcNow;
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            _logger.LogWarning(
                "Reframe submit failed after retries. CorrelationId: {CorrelationId}, Code: {Code}, Message: {Message}",
                message.CorrelationId,
                result.Code,
                result.Message);

            // Publish failure so ImageFailedConsumer can notify FE to drop the pending tile.
            await context.Publish(new ImageGenerationFailed
            {
                CorrelationId = message.CorrelationId,
                KieTaskId = null,
                ErrorCode = result.Code,
                ErrorMessage = result.Message,
                FailedAt = DateTimeExtensions.PostgreSqlUtcNow
            }, context.CancellationToken);
        }
    }
}
