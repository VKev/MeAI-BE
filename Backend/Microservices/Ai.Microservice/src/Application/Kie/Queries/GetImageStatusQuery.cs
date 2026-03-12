using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Kie.Queries;

public sealed record GetImageStatusQuery(Guid UserId, Guid CorrelationId) : IRequest<Result<ImageTaskStatusResponse>>;

public sealed record ImageTaskStatusResponse(
    Guid Id,
    Guid CorrelationId,
    string? KieTaskId,
    string Status,
    string? ResultUrls,
    string Resolution,
    string OutputFormat,
    int? ErrorCode,
    string? ErrorMessage,
    DateTime CreatedAt,
    DateTime? CompletedAt);

public sealed class GetImageStatusQueryHandler
    : IRequestHandler<GetImageStatusQuery, Result<ImageTaskStatusResponse>>
{
    private readonly IImageTaskRepository _imageTaskRepository;

    public GetImageStatusQueryHandler(IImageTaskRepository imageTaskRepository)
    {
        _imageTaskRepository = imageTaskRepository;
    }

    public async Task<Result<ImageTaskStatusResponse>> Handle(
        GetImageStatusQuery request,
        CancellationToken cancellationToken)
    {
        var task = await _imageTaskRepository.GetByCorrelationIdAsync(request.CorrelationId, cancellationToken);

        if (task is null)
        {
            return Result.Failure<ImageTaskStatusResponse>(KieErrors.TaskNotFound);
        }

        if (task.UserId != request.UserId)
        {
            return Result.Failure<ImageTaskStatusResponse>(KieErrors.Unauthorized);
        }

        return Result.Success(new ImageTaskStatusResponse(
            Id: task.Id,
            CorrelationId: task.CorrelationId,
            KieTaskId: task.KieTaskId,
            Status: task.Status,
            ResultUrls: task.ResultUrls,
            Resolution: task.Resolution,
            OutputFormat: task.OutputFormat,
            ErrorCode: task.ErrorCode,
            ErrorMessage: task.ErrorMessage,
            CreatedAt: task.CreatedAt,
            CompletedAt: task.CompletedAt));
    }
}
