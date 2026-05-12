using Application.Recommendations.Commands;
using Application.Recommendations.Models;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Recommendations.Queries;

public sealed record GetDraftPostTaskQuery(
    Guid UserId,
    Guid Id) : IRequest<Result<DraftPostTaskResponse>>;

public sealed class GetDraftPostTaskQueryHandler
    : IRequestHandler<GetDraftPostTaskQuery, Result<DraftPostTaskResponse>>
{
    private readonly IDraftPostTaskRepository _repository;

    public GetDraftPostTaskQueryHandler(IDraftPostTaskRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<DraftPostTaskResponse>> Handle(
        GetDraftPostTaskQuery request,
        CancellationToken cancellationToken)
    {
        var task = await _repository.GetByCorrelationIdOrResultPostIdAsync(request.Id, cancellationToken);
        if (task is null)
        {
            return Result.Failure<DraftPostTaskResponse>(
                new Error("DraftPost.NotFound", "Draft post task not found."));
        }
        if (task.UserId != request.UserId)
        {
            return Result.Failure<DraftPostTaskResponse>(
                new Error("DraftPost.Unauthorized", "Draft post task does not belong to the requesting user."));
        }

        return Result.Success(StartDraftPostGenerationCommandHandler.MapToResponse(task));
    }
}
