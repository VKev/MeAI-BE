using Application.Configs.Commands;
using Application.Configs.Models;
using Application.Configs.Queries;
using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Attributes;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace WebApi.Controllers;

[ApiController]
[Route("api/User/admin/config")]
[Authorize("ADMIN", "Admin")]
public sealed class AdminConfigController : ApiController
{
    private readonly IMapper _mapper;

    public AdminConfigController(IMediator mediator, IMapper mapper)
        : base(mediator)
    {
        _mapper = mapper;
    }

    [HttpGet]
    [ProducesResponseType(typeof(Result<ConfigResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetConfigQuery(), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPut]
    [ProducesResponseType(typeof(Result<ConfigResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(
        [FromBody] UpdateConfigRequest request,
        CancellationToken cancellationToken)
    {
        var command = _mapper.Map<UpdateConfigCommand>(request);
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }
}

public sealed record UpdateConfigRequest(
    string? ChatModel,
    string? MediaAspectRatio,
    int? NumberOfVariances);
