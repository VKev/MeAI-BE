using Application.Veo.Models;
using Domain.Repositories;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Contracts.VideoGenerating;
using SharedLibrary.Extensions;

namespace Application.Veo.Commands;

public sealed record HandleVideoCallbackCommand(VeoCallbackPayload Payload) : IRequest<Result<bool>>;

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
        var veoTaskId = payload.Data?.TaskId;

        _logger.LogInformation(
            "Received Veo callback for task {TaskId} with code {Code}: {Message}",
            veoTaskId,
            payload.Code,
            payload.Msg);

        if (string.IsNullOrEmpty(veoTaskId))
        {
            _logger.LogWarning("Received callback without VeoTaskId");
            return Result.Success(true);
        }

        var videoTask = await _videoTaskRepository.GetByVeoTaskIdAsync(veoTaskId, cancellationToken);

        if (videoTask is null)
        {
            _logger.LogWarning("VideoTask not found for VeoTaskId: {VeoTaskId}", veoTaskId);
            return Result.Success(true);
        }

        if (payload.Code == 200)
        {
            _logger.LogInformation(
                "Video generation completed. VeoTaskId: {VeoTaskId}, CorrelationId: {CorrelationId}",
                veoTaskId,
                videoTask.CorrelationId);

            await _bus.Publish(new VideoGenerationCompleted
            {
                CorrelationId = videoTask.CorrelationId,
                VeoTaskId = veoTaskId,
                ResultUrls = payload.Data?.Info?.ResultUrls ?? "[]",
                OriginUrls = payload.Data?.Info?.OriginUrls,
                Resolution = payload.Data?.Info?.Resolution,
                CompletedAt = DateTimeExtensions.PostgreSqlUtcNow
            }, cancellationToken);
        }
        else
        {
            _logger.LogWarning(
                "Video generation failed. VeoTaskId: {VeoTaskId}, Code: {Code}",
                veoTaskId,
                payload.Code);

            await _bus.Publish(new VideoGenerationFailed
            {
                CorrelationId = videoTask.CorrelationId,
                VeoTaskId = veoTaskId,
                ErrorCode = payload.Code,
                ErrorMessage = payload.Msg ?? "Unknown error",
                FailedAt = DateTimeExtensions.PostgreSqlUtcNow
            }, cancellationToken);
        }

        return Result.Success(true);
    }
}
