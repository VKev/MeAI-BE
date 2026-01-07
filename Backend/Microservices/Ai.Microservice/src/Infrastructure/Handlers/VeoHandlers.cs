using Application.Abstractions;
using Application.Veo;
using Application.Veo.Commands;
using Application.Veo.Queries;
using Infrastructure.Context;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Contracts.VideoGenerating;
using SharedLibrary.Extensions;

namespace Infrastructure.Handlers;

public sealed class GetVideoStatusQueryHandler
    : IRequestHandler<GetVideoStatusQuery, Result<VideoTaskStatusResponse>>
{
    private readonly MyDbContext _dbContext;

    public GetVideoStatusQueryHandler(MyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<VideoTaskStatusResponse>> Handle(
        GetVideoStatusQuery request,
        CancellationToken cancellationToken)
    {
        var task = await _dbContext.VideoTasks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.CorrelationId == request.CorrelationId, cancellationToken);

        if (task is null)
        {
            return Result.Failure<VideoTaskStatusResponse>(VeoErrors.TaskNotFound);
        }

        return Result.Success(new VideoTaskStatusResponse(
            Id: task.Id,
            CorrelationId: task.CorrelationId,
            VeoTaskId: task.VeoTaskId,
            Status: task.Status,
            ResultUrls: task.ResultUrls,
            Resolution: task.Resolution,
            ErrorCode: task.ErrorCode,
            ErrorMessage: task.ErrorMessage,
            CreatedAt: task.CreatedAt,
            CompletedAt: task.CompletedAt));
    }
}

public sealed class HandleVideoCallbackCommandHandler
    : IRequestHandler<HandleVideoCallbackCommand, Result<bool>>
{
    private readonly MyDbContext _dbContext;
    private readonly IBus _bus;
    private readonly ILogger<HandleVideoCallbackCommandHandler> _logger;

    public HandleVideoCallbackCommandHandler(
        MyDbContext dbContext,
        IBus bus,
        ILogger<HandleVideoCallbackCommandHandler> logger)
    {
        _dbContext = dbContext;
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

        var videoTask = await _dbContext.VideoTasks
            .FirstOrDefaultAsync(t => t.VeoTaskId == veoTaskId, cancellationToken);

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

public sealed class GetVeoRecordInfoQueryHandler
    : IRequestHandler<GetVeoRecordInfoQuery, Result<VeoRecordInfoResult>>
{
    private readonly MyDbContext _dbContext;
    private readonly IVeoVideoService _veoVideoService;

    public GetVeoRecordInfoQueryHandler(MyDbContext dbContext, IVeoVideoService veoVideoService)
    {
        _dbContext = dbContext;
        _veoVideoService = veoVideoService;
    }

    public async Task<Result<VeoRecordInfoResult>> Handle(
        GetVeoRecordInfoQuery request,
        CancellationToken cancellationToken)
    {
        if (request.CorrelationId == Guid.Empty)
        {
            return Result.Failure<VeoRecordInfoResult>(VeoErrors.InvalidCorrelationId);
        }

        var task = await _dbContext.VideoTasks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.CorrelationId == request.CorrelationId, cancellationToken);

        if (task is null)
        {
            return Result.Failure<VeoRecordInfoResult>(VeoErrors.TaskNotFound);
        }

        if (string.IsNullOrEmpty(task.VeoTaskId))
        {
            return Result.Failure<VeoRecordInfoResult>(VeoErrors.TaskNotCompleted);
        }

        var result = await _veoVideoService.GetVideoDetailsAsync(task.VeoTaskId, cancellationToken);

        if (!result.Success)
        {
            return Result.Failure<VeoRecordInfoResult>(VeoErrors.ApiError(result.Code, result.Message));
        }

        return Result.Success(result);
    }
}

public sealed class Get1080PVideoQueryHandler
    : IRequestHandler<Get1080PVideoQuery, Result<Veo1080PResult>>
{
    private readonly MyDbContext _dbContext;
    private readonly IVeoVideoService _veoVideoService;

    public Get1080PVideoQueryHandler(MyDbContext dbContext, IVeoVideoService veoVideoService)
    {
        _dbContext = dbContext;
        _veoVideoService = veoVideoService;
    }

    public async Task<Result<Veo1080PResult>> Handle(
        Get1080PVideoQuery request,
        CancellationToken cancellationToken)
    {
        if (request.CorrelationId == Guid.Empty)
        {
            return Result.Failure<Veo1080PResult>(VeoErrors.InvalidCorrelationId);
        }

        var task = await _dbContext.VideoTasks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.CorrelationId == request.CorrelationId, cancellationToken);

        if (task is null)
        {
            return Result.Failure<Veo1080PResult>(VeoErrors.TaskNotFound);
        }

        if (string.IsNullOrEmpty(task.VeoTaskId))
        {
            return Result.Failure<Veo1080PResult>(VeoErrors.TaskNotCompleted);
        }

        var result = await _veoVideoService.Get1080PVideoAsync(task.VeoTaskId, request.Index, cancellationToken);

        if (!result.Success)
        {
            return Result.Failure<Veo1080PResult>(VeoErrors.ApiError(result.Code, result.Message));
        }

        return Result.Success(result);
    }
}

public sealed class ExtendVideoCommandHandler
    : IRequestHandler<ExtendVideoCommand, Result<ExtendVideoCommandResponse>>
{
    private readonly MyDbContext _dbContext;
    private readonly IBus _bus;

    public ExtendVideoCommandHandler(MyDbContext dbContext, IBus bus)
    {
        _dbContext = dbContext;
        _bus = bus;
    }

    public async Task<Result<ExtendVideoCommandResponse>> Handle(
        ExtendVideoCommand request,
        CancellationToken cancellationToken)
    {
        if (request.OriginalCorrelationId == Guid.Empty)
        {
            return Result.Failure<ExtendVideoCommandResponse>(VeoErrors.InvalidCorrelationId);
        }

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return Result.Failure<ExtendVideoCommandResponse>(VeoErrors.InvalidPrompt);
        }

        var originalTask = await _dbContext.VideoTasks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.CorrelationId == request.OriginalCorrelationId, cancellationToken);

        if (originalTask is null)
        {
            return Result.Failure<ExtendVideoCommandResponse>(VeoErrors.TaskNotFound);
        }

        if (string.IsNullOrEmpty(originalTask.VeoTaskId))
        {
            return Result.Failure<ExtendVideoCommandResponse>(VeoErrors.TaskNotCompleted);
        }

        var correlationId = Guid.CreateVersion7();

        var message = new VideoExtensionStarted
        {
            CorrelationId = correlationId,
            OriginalVeoTaskId = originalTask.VeoTaskId,
            Prompt = request.Prompt,
            Seeds = request.Seeds,
            Watermark = request.Watermark,
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _bus.Publish(message, cancellationToken);

        return Result.Success(new ExtendVideoCommandResponse(correlationId));
    }
}

