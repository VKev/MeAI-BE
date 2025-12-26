using System.Security.Claims;
using Application.Resources.Commands.CreateResource;
using Application.Resources.Commands.DeleteResource;
using Application.Resources.Commands.UpdateResource;
using Application.Resources.Queries.GetResourceById;
using Application.Resources.Queries.GetResources;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Attributes;
using SharedLibrary.Common;

namespace WebApi.Controllers;

[Route("[controller]")]
[Authorize]
public sealed class ResourcesController(IMediator mediator) : ApiController(mediator)
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { message = "Unauthorized" });
        }

        var result = await _mediator.Send(new GetResourcesQuery(userId), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { message = "Unauthorized" });
        }

        var result = await _mediator.Send(new GetResourceByIdQuery(id, userId), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateResourceRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { message = "Unauthorized" });
        }

        var command = new CreateResourceCommand(
            userId,
            request.Link,
            request.Status,
            request.ResourceType,
            request.ContentType);

        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateResourceRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { message = "Unauthorized" });
        }

        var command = new UpdateResourceCommand(
            id,
            userId,
            request.Link,
            request.Status,
            request.ResourceType,
            request.ContentType);

        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { message = "Unauthorized" });
        }

        var result = await _mediator.Send(new DeleteResourceCommand(id, userId), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return NoContent();
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
