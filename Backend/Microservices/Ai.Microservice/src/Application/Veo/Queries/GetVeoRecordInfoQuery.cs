using Application.Abstractions;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Veo.Queries;

public sealed record GetVeoRecordInfoQuery(Guid UserId, Guid CorrelationId) : IRequest<Result<VeoRecordInfoResult>>;

public sealed class GetVeoRecordInfoQueryHandler
    : IRequestHandler<GetVeoRecordInfoQuery, Result<VeoRecordInfoResult>>
{
    private readonly IVideoTaskRepository _videoTaskRepository;
    private readonly IVeoVideoService _veoVideoService;

    public GetVeoRecordInfoQueryHandler(IVideoTaskRepository videoTaskRepository, IVeoVideoService veoVideoService)
    {
        _videoTaskRepository = videoTaskRepository;
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

        var task = await _videoTaskRepository.GetByCorrelationIdAsync(request.CorrelationId, cancellationToken);

        if (task is null)
        {
            return Result.Failure<VeoRecordInfoResult>(VeoErrors.TaskNotFound);
        }

        if (task.UserId != request.UserId)
        {
            return Result.Failure<VeoRecordInfoResult>(VeoErrors.Unauthorized);
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
