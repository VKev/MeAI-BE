using Application.Abstractions;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Veo.Queries;

public sealed record Get1080PVideoQuery(Guid UserId, Guid CorrelationId, int Index = 0) : IRequest<Result<Veo1080PResult>>;

public sealed class Get1080PVideoQueryHandler
    : IRequestHandler<Get1080PVideoQuery, Result<Veo1080PResult>>
{
    private readonly IVideoTaskRepository _videoTaskRepository;
    private readonly IVeoVideoService _veoVideoService;

    public Get1080PVideoQueryHandler(IVideoTaskRepository videoTaskRepository, IVeoVideoService veoVideoService)
    {
        _videoTaskRepository = videoTaskRepository;
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

        var task = await _videoTaskRepository.GetByCorrelationIdAsync(request.CorrelationId, cancellationToken);

        if (task is null)
        {
            return Result.Failure<Veo1080PResult>(VeoErrors.TaskNotFound);
        }

        if (task.UserId != request.UserId)
        {
            return Result.Failure<Veo1080PResult>(VeoErrors.Unauthorized);
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
