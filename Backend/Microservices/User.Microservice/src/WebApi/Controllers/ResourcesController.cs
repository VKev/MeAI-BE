using System.Security.Claims;
using Application.Resources.Commands;
using Application.Resources.Models;
using Application.Resources.Queries;
using Application.Users.Models;
using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Attributes;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace WebApi.Controllers;

[ApiController]
[Route("api/User/resources")]
[Authorize]
public sealed class ResourcesController : ApiController
{
    private readonly IMapper _mapper;

    public ResourcesController(IMediator mediator, IMapper mapper)
        : base(mediator)
    {
        _mapper = mapper;
    }

    [HttpGet]
    [ProducesResponseType(typeof(Result<List<ResourceResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAll(
        [FromQuery] DateTime? cursorCreatedAt,
        [FromQuery] Guid? cursorId,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(
            new GetResourcesQuery(userId, cursorCreatedAt, cursorId, limit),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Result<ResourceResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(new GetResourceByIdQuery(id, userId), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(Result<ResourceResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateResourceRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var command = _mapper.Map<CreateResourceCommand>(request) with { UserId = userId };
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Result<ResourceResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateResourceRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var command = _mapper.Map<UpdateResourceCommand>(request) with
        {
            ResourceId = id,
            UserId = userId
        };
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(new DeleteResourceCommand(id, userId), cancellationToken);
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
}

public sealed record CreateResourceRequest(
    string Link,
    string? Status,
    string? ResourceType,
    string? ContentType);

public sealed record UpdateResourceRequest(
    string Link,
    string? Status,
    string? ResourceType,
    string? ContentType);
