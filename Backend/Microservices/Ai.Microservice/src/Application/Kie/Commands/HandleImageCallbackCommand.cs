using Application.Kie.Models;
using Domain.Repositories;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Contracts.ImageGenerating;
using SharedLibrary.Extensions;
using System.Text.Json;

namespace Application.Kie.Commands;

public sealed record HandleImageCallbackCommand(Guid CorrelationId, KieCallbackPayload Payload) : IRequest<Result<bool>>;

public sealed class HandleImageCallbackCommandHandler
    : IRequestHandler<HandleImageCallbackCommand, Result<bool>>
{
    private readonly IImageTaskRepository _imageTaskRepository;
    private readonly IBus _bus;
    private readonly ILogger<HandleImageCallbackCommandHandler> _logger;

    public HandleImageCallbackCommandHandler(
        IImageTaskRepository imageTaskRepository,
        IBus bus,
        ILogger<HandleImageCallbackCommandHandler> logger)
    {
        _imageTaskRepository = imageTaskRepository;
        _bus = bus;
        _logger = logger;
    }

    public async Task<Result<bool>> Handle(
        HandleImageCallbackCommand request,
        CancellationToken cancellationToken)
    {
        var payload = request.Payload;
        var correlationId = request.CorrelationId;
        var kieTaskId = payload.Data?.TaskId;

        _logger.LogInformation(
            "Received Kie callback for CorrelationId {CorrelationId}, TaskId {TaskId} with code {Code}: {Message}",
            correlationId,
            kieTaskId,
            payload.Code,
            payload.Msg);

        // Fast lookup using correlationId (indexed)
        var imageTask = await _imageTaskRepository.GetByCorrelationIdForUpdateAsync(correlationId, cancellationToken);

        if (imageTask is null)
        {
            _logger.LogWarning("ImageTask not found for CorrelationId: {CorrelationId}", correlationId);
            return Result.Success(true);
        }

        // Verify KieTaskId matches for security
        if (!string.IsNullOrEmpty(kieTaskId) && !string.IsNullOrEmpty(imageTask.KieTaskId) && imageTask.KieTaskId != kieTaskId)
        {
            _logger.LogWarning(
                "KieTaskId mismatch. Expected: {ExpectedTaskId}, Received: {ReceivedTaskId}, CorrelationId: {CorrelationId}",
                imageTask.KieTaskId,
                kieTaskId,
                correlationId);
            return Result.Success(true);
        }

        // Check payload code or data.State
        bool isSuccess = payload.Code == 200 && payload.Data?.State == "success";
        // Also consider if code is 200 but state is 'fail'
        if (payload.Code == 200 && payload.Data?.State == "fail")
        {
            isSuccess = false;
        }

        if (isSuccess)
        {
            _logger.LogInformation(
                "Image generation completed. KieTaskId: {KieTaskId}, CorrelationId: {CorrelationId}",
                kieTaskId,
                correlationId);

            List<string> resultUrls = new();
            if (!string.IsNullOrEmpty(payload.Data?.ResultJson))
            {
                try
                {
                    var resultObj = JsonSerializer.Deserialize<KieResultJson>(payload.Data.ResultJson, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (resultObj?.ResultUrls != null)
                    {
                        resultUrls = resultObj.ResultUrls;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to parse ResultJson for CorrelationId {CorrelationId}", correlationId);
                }
            }

            await _bus.Publish(new ImageGenerationCompleted
            {
                CorrelationId = correlationId,
                KieTaskId = kieTaskId ?? imageTask.KieTaskId,
                ResultUrls = resultUrls,
                CompletedAt = DateTimeExtensions.PostgreSqlUtcNow
            }, cancellationToken);
        }
        else
        {
            _logger.LogWarning(
                "Image generation failed. KieTaskId: {KieTaskId}, CorrelationId: {CorrelationId}, Code: {Code}, FailMsg: {FailMsg}",
                kieTaskId,
                correlationId,
                payload.Code,
                payload.Data?.FailMsg);

            await _bus.Publish(new ImageGenerationFailed
            {
                CorrelationId = correlationId,
                KieTaskId = kieTaskId ?? imageTask.KieTaskId,
                ErrorCode = payload.Code,
                ErrorMessage = payload.Data?.FailMsg ?? payload.Msg ?? "Unknown error",
                FailedAt = DateTimeExtensions.PostgreSqlUtcNow
            }, cancellationToken);
        }

        return Result.Success(true);
    }
}
