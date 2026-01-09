using System.Text.Json;
using Application.Abstractions;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Veo.Commands;

public sealed record RefreshVideoStatusCommand(Guid UserId, Guid CorrelationId) : IRequest<Result<RefreshVideoStatusResponse>>;

public sealed record RefreshVideoStatusResponse(
    Guid Id,
    Guid CorrelationId,
    string? VeoTaskId,
    string Status,
    string? ResultUrls,
    string? Resolution,
    int? ErrorCode,
    string? ErrorMessage,
    DateTime CreatedAt,
    DateTime? CompletedAt);

public sealed class RefreshVideoStatusCommandHandler
    : IRequestHandler<RefreshVideoStatusCommand, Result<RefreshVideoStatusResponse>>
{
    private readonly IVideoTaskRepository _videoTaskRepository;
    private readonly IVeoVideoService _veoVideoService;

    public RefreshVideoStatusCommandHandler(
        IVideoTaskRepository videoTaskRepository,
        IVeoVideoService veoVideoService)
    {
        _videoTaskRepository = videoTaskRepository;
        _veoVideoService = veoVideoService;
    }

    public async Task<Result<RefreshVideoStatusResponse>> Handle(
        RefreshVideoStatusCommand request,
        CancellationToken cancellationToken)
    {
        if (request.CorrelationId == Guid.Empty)
        {
            return Result.Failure<RefreshVideoStatusResponse>(VeoErrors.InvalidCorrelationId);
        }

        // Use ForUpdate to enable change tracking for updates
        var task = await _videoTaskRepository.GetByCorrelationIdForUpdateAsync(request.CorrelationId, cancellationToken);

        if (task is null)
        {
            return Result.Failure<RefreshVideoStatusResponse>(VeoErrors.TaskNotFound);
        }

        if (task.UserId != request.UserId)
        {
            return Result.Failure<RefreshVideoStatusResponse>(VeoErrors.Unauthorized);
        }

        if (string.IsNullOrEmpty(task.VeoTaskId))
        {
            return Result.Failure<RefreshVideoStatusResponse>(VeoErrors.TaskNotCompleted);
        }

        // Fetch latest status from Veo API
        var result = await _veoVideoService.GetVideoDetailsAsync(task.VeoTaskId, cancellationToken);

        if (!result.Success)
        {
            return Result.Failure<RefreshVideoStatusResponse>(VeoErrors.ApiError(result.Code, result.Message));
        }

        // Update task with latest data from Veo API
        if (result.Data is not null)
        {
            var data = result.Data;

            // Update status based on SuccessFlag
            if (data.SuccessFlag == 1)
            {
                task.Status = "Completed";
                task.CompletedAt = data.CompleteTime ?? DateTime.UtcNow;
            }
            else if (data.ErrorCode.HasValue && data.ErrorCode.Value != 0)
            {
                task.Status = "Failed";
                task.ErrorCode = data.ErrorCode;
                task.ErrorMessage = data.ErrorMessage;
            }
            else
            {
                task.Status = "Processing";
            }

            // Update result URLs and resolution if available
            if (data.Response is not null)
            {
                if (data.Response.ResultUrls is not null && data.Response.ResultUrls.Any())
                {
                    task.ResultUrls = JsonSerializer.Serialize(data.Response.ResultUrls);
                }

                if (!string.IsNullOrEmpty(data.Response.Resolution))
                {
                    task.Resolution = data.Response.Resolution;
                }
            }

            await _videoTaskRepository.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(new RefreshVideoStatusResponse(
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

