using System.Text.Json;
using Application.Abstractions.Kie;
using Domain.Entities;
using Infrastructure.Context;
using Infrastructure.Logic.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedLibrary.Contracts.ImageGenerating;
using SharedLibrary.Extensions;

namespace Infrastructure.Logic.Consumers;

public class SubmitImageTaskConsumer : IConsumer<ImageGenerationStarted>
{
    private static readonly TimeSpan RecordInfoRetryDelay = TimeSpan.FromSeconds(2);
    private const int MaxRecordInfoAttempts = 3;

    private readonly IKieImageService _kieImageService;
    private readonly IKieFallbackCallbackService _kieFallbackCallbackService;
    private readonly MyDbContext _dbContext;
    private readonly ILogger<SubmitImageTaskConsumer> _logger;

    public SubmitImageTaskConsumer(
        IKieImageService kieImageService,
        IKieFallbackCallbackService kieFallbackCallbackService,
        MyDbContext dbContext,
        ILogger<SubmitImageTaskConsumer> logger)
    {
        _kieImageService = kieImageService;
        _kieFallbackCallbackService = kieFallbackCallbackService;
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
            WorkspaceId = message.WorkspaceId,
            CorrelationId = message.CorrelationId,
            Prompt = message.Prompt,
            AspectRatio = message.AspectRatio,
            Resolution = message.Resolution,
            OutputFormat = message.OutputFormat,
            Status = "Submitted",
            SocialTargetsJson = message.SocialTargets is { Count: > 0 }
                ? JsonSerializer.Serialize(message.SocialTargets)
                : null,
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        _dbContext.ImageTasks.Add(imageTask);
        await _dbContext.SaveChangesAsync(context.CancellationToken);

        var request = new KieGenerateRequest(
            Prompt: message.Prompt,
            ImageInput: message.ImageUrls,
            Model: message.Model,
            AspectRatio: message.AspectRatio,
            Resolution: message.Resolution,
            OutputFormat: message.OutputFormat,
            NumberOfVariances: message.NumberOfVariances,
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

            // Kie uses callbacks — no need to poll for completion.
            // Only check once for immediate rejection (credit/quota issues).
            await TryHandleProviderAcceptedButUnprocessableAsync(
                message.CorrelationId,
                result.TaskId,
                message.NumberOfVariances,
                context.CancellationToken);
        }
        else
        {
            var fallbackTaskId = $"fallback-{message.CorrelationId:N}";
            imageTask.KieTaskId = fallbackTaskId;
            imageTask.Status = "Processing";
            imageTask.ErrorCode = null;
            imageTask.ErrorMessage = null;
            imageTask.CompletedAt = null;
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            _logger.LogWarning(
                "Image generation submit failed. Fallback flow is scheduled with simulated callback delay. CorrelationId: {CorrelationId}, Code: {Code}, Message: {Message}",
                message.CorrelationId,
                result.Code,
                result.Message);

            await context.Publish(new ImageTaskCreated
            {
                CorrelationId = message.CorrelationId,
                KieTaskId = fallbackTaskId,
                CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
            });

            var callbackSent = await _kieFallbackCallbackService.SendImageSuccessCallbackAsync(
                message.CorrelationId,
                fallbackTaskId,
                message.NumberOfVariances,
                context.CancellationToken);

            if (callbackSent)
            {
                return;
            }

            imageTask.Status = "Failed";
            imageTask.ErrorCode = result.Code;
            imageTask.ErrorMessage = $"{result.Message}. Fallback callback dispatch failed.";
            imageTask.CompletedAt = DateTimeExtensions.PostgreSqlUtcNow;
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            await context.Publish(new ImageGenerationFailed
            {
                CorrelationId = message.CorrelationId,
                KieTaskId = fallbackTaskId,
                ErrorCode = result.Code,
                ErrorMessage = imageTask.ErrorMessage,
                FailedAt = DateTimeExtensions.PostgreSqlUtcNow
            });
        }
    }

    private async Task TryHandleProviderAcceptedButUnprocessableAsync(
        Guid correlationId,
        string kieTaskId,
        int numberOfVariances,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxRecordInfoAttempts; attempt++)
        {
            var recordInfo = await _kieImageService.GetImageDetailsAsync(kieTaskId, cancellationToken);
            if (ShouldFallbackFromRecordInfo(recordInfo))
            {
                var task = await _dbContext.ImageTasks
                    .FirstOrDefaultAsync(t => t.CorrelationId == correlationId, cancellationToken);

                if (task is null || !string.Equals(task.Status, "Processing", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _logger.LogWarning(
                    "Kie task accepted on submit but appears unprocessable. Triggering fallback callback. CorrelationId: {CorrelationId}, KieTaskId: {KieTaskId}, RecordState: {State}, Message: {Message}",
                    correlationId,
                    kieTaskId,
                    recordInfo.Data?.State,
                    recordInfo.Data?.FailMsg ?? recordInfo.Message);

                var callbackSent = await _kieFallbackCallbackService.SendImageSuccessCallbackAsync(
                    correlationId,
                    kieTaskId,
                    numberOfVariances,
                    cancellationToken);

                if (!callbackSent)
                {
                    _logger.LogWarning(
                        "Fallback callback dispatch after record-info failure did not succeed. CorrelationId: {CorrelationId}, KieTaskId: {KieTaskId}",
                        correlationId,
                        kieTaskId);
                }

                return;
            }

            if (!ShouldRetryRecordInfo(recordInfo) || attempt == MaxRecordInfoAttempts)
            {
                // Task is still in a waiting/processing state — Kie will call back when done.
                // Do NOT trigger fallback here; let the callback arrive naturally.
                return;
            }

            try
            {
                await Task.Delay(RecordInfoRetryDelay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task TryTriggerTimeoutFallbackAsync(
        Guid correlationId,
        string kieTaskId,
        int numberOfVariances,
        string? lastKnownState,
        CancellationToken cancellationToken)
    {
        var task = await _dbContext.ImageTasks
            .FirstOrDefaultAsync(t => t.CorrelationId == correlationId, cancellationToken);

        if (task is null || !string.Equals(task.Status, "Processing", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _logger.LogWarning(
            "Kie task remained in non-terminal state after retry window. Forcing fallback callback. CorrelationId: {CorrelationId}, KieTaskId: {KieTaskId}, LastState: {LastState}, Attempts: {Attempts}, DelaySeconds: {DelaySeconds}",
            correlationId,
            kieTaskId,
            lastKnownState ?? "unknown",
            MaxRecordInfoAttempts,
            RecordInfoRetryDelay.TotalSeconds);

        var callbackSent = await _kieFallbackCallbackService.SendImageSuccessCallbackAsync(
            correlationId,
            kieTaskId,
            numberOfVariances,
            cancellationToken);

        if (!callbackSent)
        {
            _logger.LogWarning(
                "Fallback callback dispatch after stalled state did not succeed. CorrelationId: {CorrelationId}, KieTaskId: {KieTaskId}",
                correlationId,
                kieTaskId);
        }
    }

    private static bool ShouldFallbackFromRecordInfo(KieRecordInfoResult recordInfo)
    {
        var message = recordInfo.Message ?? string.Empty;
        var state = recordInfo.Data?.State ?? string.Empty;
        var failMsg = recordInfo.Data?.FailMsg ?? string.Empty;

        if (!recordInfo.Success &&
            ContainsCreditOrQuotaSignal(message))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(failMsg) &&
            ContainsCreditOrQuotaSignal(failMsg))
        {
            return true;
        }

        if (IsFailedState(state))
        {
            return true;
        }

        return false;
    }

    private static bool ShouldRetryRecordInfo(KieRecordInfoResult recordInfo)
    {
        if (!recordInfo.Success)
        {
            return true;
        }

        var state = recordInfo.Data?.State;
        if (string.IsNullOrWhiteSpace(state))
        {
            return true;
        }

        var normalized = state.Trim().ToLowerInvariant();
        return normalized is "waiting" or "queued" or "queueing" or "processing" or "running" or "submitted";
    }

    private static bool IsFailedState(string state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return false;
        }

        var normalized = state.Trim().ToLowerInvariant();
        return normalized is "fail" or "failed" or "error" or "canceled" or "cancelled";
    }

    private static bool ContainsCreditOrQuotaSignal(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.ToLowerInvariant();
        return normalized.Contains("insufficient")
               || normalized.Contains("credit")
               || normalized.Contains("quota")
               || normalized.Contains("balance");
    }
}
