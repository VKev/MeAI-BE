using System.Security.Claims;
using Application.Agents.Commands;
using Application.Agents.Models;
using Application.Agents.Queries;
using Application.Abstractions.Agents;
using Application.PublishingSchedules.Models;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Attributes;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace WebApi.Controllers;

[ApiController]
[Route("api/Ai/agent/sessions")]
[Authorize]
public sealed class AgentSessionsController : ApiController
{
    public AgentSessionsController(IMediator mediator) : base(mediator)
    {
    }

    [HttpGet("{sessionId:guid}/messages")]
    [ProducesResponseType(typeof(Result<IReadOnlyList<AgentMessageResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetMessages(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var result = await _mediator.Send(
            new GetAgentSessionMessagesQuery(sessionId, userId),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPost("{sessionId:guid}/messages")]
    [ProducesResponseType(typeof(Result<AgentChatResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SendMessage(
        Guid sessionId,
        [FromBody] AgentMessageRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        if (request is null)
        {
            return HandleFailure(Result.Failure<AgentChatResponse>(
                new Error("Agent.InvalidRequest", "Request body is required.")));
        }

        var result = await _mediator.Send(
            new SendAgentMessageCommand(
                sessionId,
                userId,
                request.Message,
                request.ImageOptions is null
                    ? null
                    : new AgentImageOptions(
                        request.ImageOptions.Model,
                        request.ImageOptions.AspectRatio,
                        request.ImageOptions.Resolution,
                        request.ImageOptions.NumberOfVariances,
                        request.ImageOptions.SocialTargets?
                            .Where(target => target is not null &&
                                             !string.IsNullOrWhiteSpace(target.Platform) &&
                                             !string.IsNullOrWhiteSpace(target.Type) &&
                                             !string.IsNullOrWhiteSpace(target.Ratio))
                            .Select(target => new AgentSocialTarget(
                                target.Platform!.Trim(),
                                target.Type!.Trim(),
                                target.Ratio!.Trim()))
                            .ToList()),
                request.VideoOptions is null
                    ? null
                    : new AgentVideoOptions(
                        request.VideoOptions.Model,
                        request.VideoOptions.AspectRatio,
                        request.VideoOptions.Seeds,
                        request.VideoOptions.EnableTranslation,
                        request.VideoOptions.Watermark,
                        request.VideoOptions.ResourceIds?
                            .Where(id => id != Guid.Empty)
                            .Distinct()
                            .ToList()),
                request.ScheduleOptions is null
                    ? null
                    : new AgentScheduleOptions(
                        request.ScheduleOptions.ExecuteAtUtc,
                        request.ScheduleOptions.Timezone,
                        request.ScheduleOptions.MaxContentLength,
                        request.ScheduleOptions.Targets?
                            .Where(target => target is not null && target.SocialMediaId != Guid.Empty)
                            .Select(target => new PublishingScheduleTargetInput(
                                target.SocialMediaId,
                                target.IsPrimary))
                            .ToList())),
            cancellationToken);

        if (result.IsFailure)
        {
            return MapBillingFailureOrDefault(result);
        }

        return Ok(result);
    }

    private IActionResult MapBillingFailureOrDefault(Result result)
    {
        if (string.Equals(result.Error.Code, "Billing.InsufficientFunds", StringComparison.Ordinal))
        {
            return StatusCode(
                StatusCodes.Status402PaymentRequired,
                new ProblemDetails
                {
                    Status = StatusCodes.Status402PaymentRequired,
                    Type = result.Error.Code,
                    Detail = result.Error.Description
                });
        }

        return HandleFailure(result);
    }

    private bool TryGetUserId(out Guid userId)
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claimValue, out userId);
    }
}

public sealed record AgentMessageRequest(
    string? Message,
    AgentImageOptionsRequest? ImageOptions = null,
    AgentVideoOptionsRequest? VideoOptions = null,
    AgentScheduleOptionsRequest? ScheduleOptions = null);

public sealed record AgentImageOptionsRequest(
    string? Model,
    string? AspectRatio,
    string? Resolution,
    int? NumberOfVariances,
    List<AgentSocialTargetRequest>? SocialTargets = null);

public sealed record AgentSocialTargetRequest(
    string? Platform,
    string? Type,
    string? Ratio);

public sealed record AgentVideoOptionsRequest(
    string? Model,
    string? AspectRatio,
    int? Seeds,
    bool? EnableTranslation,
    string? Watermark,
    List<Guid>? ResourceIds = null);

public sealed record AgentScheduleOptionsRequest(
    DateTime ExecuteAtUtc,
    string? Timezone,
    int? MaxContentLength,
    List<PublishingScheduleTargetInputRequest>? Targets = null);

public sealed record PublishingScheduleTargetInputRequest(
    Guid SocialMediaId,
    bool? IsPrimary = null);
