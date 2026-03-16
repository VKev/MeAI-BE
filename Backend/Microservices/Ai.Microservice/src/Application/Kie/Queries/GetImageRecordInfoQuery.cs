using Application.Abstractions.Kie;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Kie.Queries;

public sealed record GetImageRecordInfoQuery(Guid UserId, Guid CorrelationId) : IRequest<Result<KieRecordInfoResult>>;

public sealed class GetImageRecordInfoQueryHandler
    : IRequestHandler<GetImageRecordInfoQuery, Result<KieRecordInfoResult>>
{
    private readonly IImageTaskRepository _imageTaskRepository;
    private readonly IKieImageService _kieImageService;

    public GetImageRecordInfoQueryHandler(
        IImageTaskRepository imageTaskRepository,
        IKieImageService kieImageService)
    {
        _imageTaskRepository = imageTaskRepository;
        _kieImageService = kieImageService;
    }

    public async Task<Result<KieRecordInfoResult>> Handle(
        GetImageRecordInfoQuery request,
        CancellationToken cancellationToken)
    {
        if (request.CorrelationId == Guid.Empty)
        {
            return Result.Failure<KieRecordInfoResult>(KieErrors.InvalidCorrelationId);
        }

        var task = await _imageTaskRepository.GetByCorrelationIdAsync(request.CorrelationId, cancellationToken);

        if (task is null)
        {
            return Result.Failure<KieRecordInfoResult>(KieErrors.TaskNotFound);
        }

        if (task.UserId != request.UserId)
        {
            return Result.Failure<KieRecordInfoResult>(KieErrors.Unauthorized);
        }

        if (string.IsNullOrWhiteSpace(task.KieTaskId))
        {
            return Result.Failure<KieRecordInfoResult>(KieErrors.TaskNotCompleted);
        }

        var result = await _kieImageService.GetImageDetailsAsync(task.KieTaskId, cancellationToken);
        if (!result.Success)
        {
            return Result.Failure<KieRecordInfoResult>(KieErrors.ApiError(result.Code, result.Message));
        }

        return Result.Success(result);
    }
}
