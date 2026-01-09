using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Veo.Queries;

public sealed record GetUserVideoTasksQuery(Guid UserId) : IRequest<Result<IEnumerable<UserVideoTaskResponse>>>;

public sealed record UserVideoTaskResponse(
    Guid Id,
    Guid CorrelationId,
    string? VeoTaskId,
    string Prompt,
    string Model,
    string AspectRatio,
    string Status,
    string? ResultUrls,
    string? Resolution,
    int? ErrorCode,
    string? ErrorMessage,
    DateTime CreatedAt,
    DateTime? CompletedAt);

public sealed class GetUserVideoTasksQueryHandler
    : IRequestHandler<GetUserVideoTasksQuery, Result<IEnumerable<UserVideoTaskResponse>>>
{
    private readonly IVideoTaskRepository _videoTaskRepository;

    public GetUserVideoTasksQueryHandler(IVideoTaskRepository videoTaskRepository)
    {
        _videoTaskRepository = videoTaskRepository;
    }

    public async Task<Result<IEnumerable<UserVideoTaskResponse>>> Handle(
        GetUserVideoTasksQuery request,
        CancellationToken cancellationToken)
    {
        var tasks = await _videoTaskRepository.GetByUserIdAsync(request.UserId, cancellationToken);

        var response = tasks.Select(task => new UserVideoTaskResponse(
            Id: task.Id,
            CorrelationId: task.CorrelationId,
            VeoTaskId: task.VeoTaskId,
            Prompt: task.Prompt,
            Model: task.Model,
            AspectRatio: task.AspectRatio,
            Status: task.Status,
            ResultUrls: task.ResultUrls,
            Resolution: task.Resolution,
            ErrorCode: task.ErrorCode,
            ErrorMessage: task.ErrorMessage,
            CreatedAt: task.CreatedAt,
            CompletedAt: task.CompletedAt));

        return Result.Success(response);
    }
}
