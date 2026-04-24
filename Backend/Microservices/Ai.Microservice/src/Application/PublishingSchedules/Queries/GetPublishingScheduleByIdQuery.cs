using Application.PublishingSchedules.Models;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.PublishingSchedules.Queries;

public sealed record GetPublishingScheduleByIdQuery(
    Guid ScheduleId,
    Guid UserId) : IRequest<Result<PublishingScheduleResponse>>;

public sealed class GetPublishingScheduleByIdQueryHandler
    : IRequestHandler<GetPublishingScheduleByIdQuery, Result<PublishingScheduleResponse>>
{
    private readonly IPublishingScheduleRepository _publishingScheduleRepository;
    private readonly PublishingScheduleResponseBuilder _responseBuilder;

    public GetPublishingScheduleByIdQueryHandler(
        IPublishingScheduleRepository publishingScheduleRepository,
        PublishingScheduleResponseBuilder responseBuilder)
    {
        _publishingScheduleRepository = publishingScheduleRepository;
        _responseBuilder = responseBuilder;
    }

    public async Task<Result<PublishingScheduleResponse>> Handle(
        GetPublishingScheduleByIdQuery request,
        CancellationToken cancellationToken)
    {
        var schedule = await _publishingScheduleRepository.GetByIdAsync(request.ScheduleId, cancellationToken);
        if (schedule is null || schedule.DeletedAt.HasValue)
        {
            return Result.Failure<PublishingScheduleResponse>(PublishingScheduleErrors.NotFound);
        }

        if (schedule.UserId != request.UserId)
        {
            return Result.Failure<PublishingScheduleResponse>(PublishingScheduleErrors.Unauthorized);
        }

        var response = await _responseBuilder.BuildAsync(schedule, cancellationToken);
        return Result.Success(response);
    }
}
