using Application.PublishingSchedules.Models;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.PublishingSchedules.Queries;

public sealed record GetUserPublishingSchedulesQuery(
    Guid UserId,
    Guid? WorkspaceId,
    string? Status,
    int? Limit) : IRequest<Result<IReadOnlyList<PublishingScheduleResponse>>>;

public sealed class GetUserPublishingSchedulesQueryHandler
    : IRequestHandler<GetUserPublishingSchedulesQuery, Result<IReadOnlyList<PublishingScheduleResponse>>>
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 100;

    private readonly IPublishingScheduleRepository _publishingScheduleRepository;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly PublishingScheduleResponseBuilder _responseBuilder;

    public GetUserPublishingSchedulesQueryHandler(
        IPublishingScheduleRepository publishingScheduleRepository,
        IWorkspaceRepository workspaceRepository,
        PublishingScheduleResponseBuilder responseBuilder)
    {
        _publishingScheduleRepository = publishingScheduleRepository;
        _workspaceRepository = workspaceRepository;
        _responseBuilder = responseBuilder;
    }

    public async Task<Result<IReadOnlyList<PublishingScheduleResponse>>> Handle(
        GetUserPublishingSchedulesQuery request,
        CancellationToken cancellationToken)
    {
        if (request.WorkspaceId.HasValue)
        {
            var exists = await _workspaceRepository.ExistsForUserAsync(
                request.WorkspaceId.Value,
                request.UserId,
                cancellationToken);

            if (!exists)
            {
                return Result.Failure<IReadOnlyList<PublishingScheduleResponse>>(PublishingScheduleErrors.WorkspaceNotFound);
            }
        }

        var limit = Math.Clamp(request.Limit ?? DefaultLimit, 1, MaxLimit);
        var schedules = await _publishingScheduleRepository.GetByUserIdAsync(
            request.UserId,
            request.WorkspaceId,
            request.Status,
            limit,
            cancellationToken);

        var response = await _responseBuilder.BuildManyAsync(schedules, cancellationToken);
        return Result.Success(response);
    }
}
