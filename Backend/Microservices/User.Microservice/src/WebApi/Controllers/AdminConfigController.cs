using Application.Configs.Commands.UpdateConfig;
using Application.Configs.Contracts;
using Application.Configs.Queries.GetConfig;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Attributes;
using SharedLibrary.Common;
using WebApi.Contracts;

namespace WebApi.Controllers;

[Route("admin/config")]
[Authorize("ADMIN")]
public sealed class AdminConfigController(IMediator mediator) : ApiController(mediator)
{
    [HttpGet]
    [ProducesResponseType(typeof(ConfigResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetConfigQuery(), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    [HttpPut]
    [ProducesResponseType(typeof(ConfigResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update([FromBody] UpdateConfigRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdateConfigCommand(
            request.ChatModel,
            request.MediaAspectRatio,
            request.NumberOfVariances), cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }
}

public sealed record UpdateConfigRequest(
    string? ChatModel,
    string? MediaAspectRatio,
    int? NumberOfVariances);
