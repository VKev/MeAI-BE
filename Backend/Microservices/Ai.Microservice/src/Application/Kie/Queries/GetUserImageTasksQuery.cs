using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Kie.Queries;

public sealed record GetUserImageTasksQuery(Guid UserId) : IRequest<Result<IEnumerable<UserImageTaskResponse>>>;

public sealed record UserImageTaskResponse(
    Guid Id,
    Guid CorrelationId,
    string? KieTaskId,
    string Prompt,
    string AspectRatio,
    string Resolution,
    string OutputFormat,
    string Status,
    string? ResultUrls,
    int? ErrorCode,
    string? ErrorMessage,
    DateTime CreatedAt,
    DateTime? CompletedAt);

public sealed class GetUserImageTasksQueryHandler
    : IRequestHandler<GetUserImageTasksQuery, Result<IEnumerable<UserImageTaskResponse>>>
{
    private readonly IImageTaskRepository _imageTaskRepository;

    public GetUserImageTasksQueryHandler(IImageTaskRepository imageTaskRepository)
    {
        _imageTaskRepository = imageTaskRepository;
    }

    public async Task<Result<IEnumerable<UserImageTaskResponse>>> Handle(
        GetUserImageTasksQuery request,
        CancellationToken cancellationToken)
    {
        var tasks = await _imageTaskRepository.GetByUserIdAsync(request.UserId, cancellationToken);

        var response = tasks.Select(task => new UserImageTaskResponse(
            Id: task.Id,
            CorrelationId: task.CorrelationId,
            KieTaskId: task.KieTaskId,
            Prompt: task.Prompt,
            AspectRatio: task.AspectRatio,
            Resolution: task.Resolution,
            OutputFormat: task.OutputFormat,
            Status: task.Status,
            ResultUrls: task.ResultUrls,
            ErrorCode: task.ErrorCode,
            ErrorMessage: task.ErrorMessage,
            CreatedAt: task.CreatedAt,
            CompletedAt: task.CompletedAt));

        return Result.Success(response);
    }
}
