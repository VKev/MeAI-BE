using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Veo.Queries;

public sealed record GetVideoStatusQuery(Guid CorrelationId) : IRequest<Result<VideoTaskStatusResponse>>;

public sealed record VideoTaskStatusResponse(
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

public sealed class GetVideoStatusQueryHandler
    : IRequestHandler<GetVideoStatusQuery, Result<VideoTaskStatusResponse>>
{
    private readonly IVideoTaskRepository _videoTaskRepository;

    public GetVideoStatusQueryHandler(IVideoTaskRepository videoTaskRepository)
    {
        _videoTaskRepository = videoTaskRepository;
    }

    public async Task<Result<VideoTaskStatusResponse>> Handle(
        GetVideoStatusQuery request,
        CancellationToken cancellationToken)
    {
        var task = await _videoTaskRepository.GetByCorrelationIdAsync(request.CorrelationId, cancellationToken);

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
