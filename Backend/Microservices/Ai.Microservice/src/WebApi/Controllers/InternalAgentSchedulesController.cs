using Application.Abstractions.Automation;
using Application.PublishingSchedules;
using Application.PublishingSchedules.Commands;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace WebApi.Controllers;

[ApiController]
[Route("api/Ai/internal/agent-schedules")]
[AllowAnonymous]
public sealed class InternalAgentSchedulesController : ApiController
{
    private readonly IConfiguration _configuration;

    public InternalAgentSchedulesController(
        IMediator mediator,
        IConfiguration configuration) : base(mediator)
    {
        _configuration = configuration;
    }

    [HttpPost("runtime-result")]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> HandleRuntimeResult(
        [FromBody] AgentScheduleRuntimeResultRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorized(Request.Headers.Authorization.ToString()))
        {
            return Unauthorized();
        }

        var result = await _mediator.Send(
            new HandleAgentScheduleRuntimeResultCommand(
                request.ScheduleId,
                request.JobId,
                new N8nWebSearchResponse(
                    request.Query,
                    request.RetrievedAtUtc,
                    request.Results.Select(item => new N8nWebSearchResultItem(
                        item.Title,
                        item.Url,
                        item.Description,
                        item.Source)).ToList(),
                    request.LlmContext),
                request.CorrelationId,
                request.AttemptNumber),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    private bool IsAuthorized(string? authorizationHeader)
    {
        var configuredToken = _configuration["N8n:InternalCallbackToken"]
                              ?? _configuration["N8n__InternalCallbackToken"];
        if (string.IsNullOrWhiteSpace(configuredToken))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(authorizationHeader))
        {
            return false;
        }

        var expected = $"Bearer {configuredToken.Trim()}";
        return string.Equals(authorizationHeader.Trim(), expected, StringComparison.Ordinal);
    }
}

public sealed record AgentScheduleRuntimeResultRequest(
    Guid JobId,
    Guid ScheduleId,
    string Query,
    DateTime RetrievedAtUtc,
    IReadOnlyList<AgentScheduleRuntimeResultItem> Results,
    string? LlmContext = null,
    Guid? CorrelationId = null,
    int? AttemptNumber = null);

public sealed record AgentScheduleRuntimeResultItem(
    string? Title,
    string? Url,
    string? Description,
    string? Source);
