using Application.Kie.Commands;
using Application.Kie.Models;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Attributes;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace WebApi.Controllers;

[ApiController]
[Route("api/Ai/image")]
[Authorize]
public sealed class ImageController : ApiController
{
    public ImageController(IMediator mediator) : base(mediator)
    {
    }

    [HttpPost("callback/{correlationId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> HandleCallback(
        Guid correlationId,
        [FromBody] KieCallbackPayload payload,
        CancellationToken cancellationToken)
    {
        var command = new HandleImageCallbackCommand(correlationId, payload);
        var result = await _mediator.Send(command, cancellationToken);

        return Ok(result);
    }
}
