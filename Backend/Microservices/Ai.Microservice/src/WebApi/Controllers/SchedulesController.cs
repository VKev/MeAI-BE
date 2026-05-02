using System.Security.Claims;
using Application.PublishingSchedules;
using Application.PublishingSchedules.Commands;
using Application.PublishingSchedules.Models;
using Application.PublishingSchedules.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Attributes;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace WebApi.Controllers;

[ApiController]
[Route("api/Ai/schedules")]
[Authorize]
public sealed class SchedulesController : ApiController
{
    public SchedulesController(IMediator mediator) : base(mediator)
    {
    }

    [HttpGet]
    [ProducesResponseType(typeof(Result<IReadOnlyList<PublishingScheduleResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? workspaceId,
        [FromQuery] string? status,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var result = await _mediator.Send(
            new GetUserPublishingSchedulesQuery(userId, workspaceId, status, limit),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpGet("{scheduleId:guid}")]
    [ProducesResponseType(typeof(Result<PublishingScheduleResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetById(Guid scheduleId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var result = await _mediator.Send(new GetPublishingScheduleByIdQuery(scheduleId, userId), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(Result<PublishingScheduleResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] UpsertPublishingScheduleRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        Result<PublishingScheduleResponse> result;
        if (IsAgenticMode(request.Mode))
        {
            return HandleFailure(Result.Failure<PublishingScheduleResponse>(PublishingScheduleErrors.UnsupportedMode));
        }
        else
        {
            result = await _mediator.Send(
                new CreatePublishingScheduleCommand(
                    userId,
                    request.WorkspaceId,
                    request.Name,
                    request.Mode,
                    request.ExecuteAtUtc,
                    request.Timezone,
                    request.IsPrivate,
                    request.Items,
                    request.Targets),
                cancellationToken);
        }

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPut("{scheduleId:guid}")]
    [ProducesResponseType(typeof(Result<PublishingScheduleResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(
        Guid scheduleId,
        [FromBody] UpsertPublishingScheduleRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        Result<PublishingScheduleResponse> result;
        if (IsAgenticMode(request.Mode))
        {
            return HandleFailure(Result.Failure<PublishingScheduleResponse>(PublishingScheduleErrors.UnsupportedMode));
        }
        else
        {
            result = await _mediator.Send(
                new UpdatePublishingScheduleCommand(
                    scheduleId,
                    userId,
                    request.WorkspaceId,
                    request.Name,
                    request.Mode,
                    request.ExecuteAtUtc,
                    request.Timezone,
                    request.IsPrivate,
                    request.Items,
                    request.Targets),
                cancellationToken);
        }

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPost("{scheduleId:guid}/cancel")]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Cancel(Guid scheduleId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var result = await _mediator.Send(
            new CancelPublishingScheduleCommand(scheduleId, userId),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPost("{scheduleId:guid}/activate")]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Activate(Guid scheduleId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var result = await _mediator.Send(
            new ActivatePublishingScheduleCommand(scheduleId, userId),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    private bool TryGetUserId(out Guid userId)
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claimValue, out userId);
    }

    private static bool IsAgenticMode(string? mode)
    {
        var normalized = (mode ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "agentic" or "agent" or "agentic_live_content_schedule";
    }
}

public sealed record UpsertPublishingScheduleRequest(
    Guid WorkspaceId,
    string? Name,
    string? Mode,
    DateTime ExecuteAtUtc,
    string? Timezone,
    bool? IsPrivate,
    string? PlatformPreference,
    string? AgentPrompt,
    PublishingScheduleSearchInput? Search,
    IReadOnlyList<PublishingScheduleItemInput>? Items,
    IReadOnlyList<PublishingScheduleTargetInput>? Targets);
