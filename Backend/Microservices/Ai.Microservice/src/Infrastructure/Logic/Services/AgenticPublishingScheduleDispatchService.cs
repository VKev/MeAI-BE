using Application.PublishingSchedules.Commands;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Logic.Services;

public sealed class AgenticPublishingScheduleDispatchService
{
    private const int BatchSize = 10;

    private readonly IPublishingScheduleRepository _publishingScheduleRepository;
    private readonly IMediator _mediator;
    private readonly ILogger<AgenticPublishingScheduleDispatchService> _logger;

    public AgenticPublishingScheduleDispatchService(
        IPublishingScheduleRepository publishingScheduleRepository,
        IMediator mediator,
        ILogger<AgenticPublishingScheduleDispatchService> logger)
    {
        _publishingScheduleRepository = publishingScheduleRepository;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<int> DispatchDueSchedulesAsync(CancellationToken cancellationToken)
    {
        var claimedScheduleIds = await _publishingScheduleRepository.ClaimDueAgenticSchedulesAsync(
            DateTime.UtcNow,
            BatchSize,
            cancellationToken);

        foreach (var scheduleId in claimedScheduleIds)
        {
            var result = await _mediator.Send(
                new ExecuteAgenticPublishingScheduleCommand(scheduleId),
                cancellationToken);

            if (result.IsFailure)
            {
                _logger.LogWarning(
                    "Agentic schedule dispatch failed. ScheduleId: {ScheduleId}, Code: {Code}, Description: {Description}",
                    scheduleId,
                    result.Error.Code,
                    result.Error.Description);
            }
        }

        return claimedScheduleIds.Count;
    }
}
