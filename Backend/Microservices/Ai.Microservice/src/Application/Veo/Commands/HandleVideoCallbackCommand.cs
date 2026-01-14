using Application.Veo.Models;
using Domain.Repositories;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Contracts.VideoGenerating;
using SharedLibrary.Extensions;

namespace Application.Veo.Commands;

public sealed record HandleVideoCallbackCommand(Guid CorrelationId, VeoCallbackPayload Payload) : IRequest<Result<bool>>;

public sealed class HandleVideoCallbackCommandHandler
    : IRequestHandler<HandleVideoCallbackCommand, Result<bool>>
{
    private readonly IVideoTaskRepository _videoTaskRepository;
    private readonly IBus _bus;
    private readonly ILogger<HandleVideoCallbackCommandHandler> _logger;

    public HandleVideoCallbackCommandHandler(
        IVideoTaskRepository videoTaskRepository,
        IBus bus,
        ILogger<HandleVideoCallbackCommandHandler> logger)
    {
        _videoTaskRepository = videoTaskRepository;
        _bus = bus;
        _logger = logger;
    }

    public async Task<Result<bool>> Handle(
        HandleVideoCallbackCommand request,
        CancellationToken cancellationToken)
    {
        var payload = request.Payload;
        var correlationId = request.CorrelationId;
        var veoTaskId = payload.Data?.TaskId;

        _logger.LogInformation(
            "Received Veo callback for CorrelationId {CorrelationId}, TaskId {TaskId} with code {Code}: {Message}",
            correlationId,
            veoTaskId,
            payload.Code,
            payload.Msg);

        // Fast lookup using correlationId (indexed)
        var videoTask = await _videoTaskRepository.GetByCorrelationIdForUpdateAsync(correlationId, cancellationToken);

        if (videoTask is null)
        {
            _logger.LogWarning("VideoTask not found for CorrelationId: {CorrelationId}", correlationId);
            return Result.Success(true);
        }

        // Verify VeoTaskId matches for security
        if (!string.IsNullOrEmpty(veoTaskId) && !string.IsNullOrEmpty(videoTask.VeoTaskId) && videoTask.VeoTaskId != veoTaskId)
        {
            _logger.LogWarning(
                "VeoTaskId mismatch. Expected: {ExpectedTaskId}, Received: {ReceivedTaskId}, CorrelationId: {CorrelationId}",
                videoTask.VeoTaskId,
                veoTaskId,
                correlationId);
            return Result.Success(true);
        }

        if (payload.Code == 200)
        {
            _logger.LogInformation(
                "Video generation completed. VeoTaskId: {VeoTaskId}, CorrelationId: {CorrelationId}",
                veoTaskId,
                correlationId);

            await _bus.Publish(new VideoGenerationCompleted
            {
                CorrelationId = correlationId,
                VeoTaskId = veoTaskId ?? videoTask.VeoTaskId ?? string.Empty,
                ResultUrls = payload.Data?.Info?.ResultUrls ?? "[]",
                OriginUrls = payload.Data?.Info?.OriginUrls,
                Resolution = payload.Data?.Info?.Resolution,
                CompletedAt = DateTimeExtensions.PostgreSqlUtcNow
            }, cancellationToken);
        }
        else
        {
            _logger.LogWarning(
                "Video generation failed. VeoTaskId: {VeoTaskId}, CorrelationId: {CorrelationId}, Code: {Code}",
                veoTaskId,
                correlationId,
                payload.Code);

            await _bus.Publish(new VideoGenerationFailed
            {
                CorrelationId = correlationId,
                VeoTaskId = veoTaskId ?? videoTask.VeoTaskId,
                ErrorCode = payload.Code,
                ErrorMessage = payload.Msg ?? "Unknown error",
                FailedAt = DateTimeExtensions.PostgreSqlUtcNow
            }, cancellationToken);
        }

        return Result.Success(true);
    }
}

