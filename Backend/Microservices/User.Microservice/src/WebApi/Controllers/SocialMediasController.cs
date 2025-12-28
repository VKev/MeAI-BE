using System.Security.Claims;
using System.Text.Json;
using Application.SocialMedias.Commands.CreateSocialMedia;
using Application.SocialMedias.Commands.DeleteSocialMedia;
using Application.SocialMedias.Commands.UpdateSocialMedia;
using Application.SocialMedias.Contracts;
using Application.SocialMedias.Queries.GetSocialMediaById;
using Application.SocialMedias.Queries.GetSocialMedias;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Attributes;
using SharedLibrary.Common;
using WebApi.Contracts;

namespace WebApi.Controllers;

[Route("[controller]")]
[Authorize]
public sealed class SocialMediasController(IMediator mediator) : ApiController(mediator)
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SocialMediaResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(new GetSocialMediasQuery(userId), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(SocialMediaResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(new GetSocialMediaByIdQuery(id, userId), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    [HttpPost]
    [ProducesResponseType(typeof(SocialMediaResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateSocialMediaRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var metadata = request.Metadata.HasValue
            ? JsonDocument.Parse(request.Metadata.Value.GetRawText())
            : null;

        var command = new CreateSocialMediaCommand(userId, request.Type, metadata);
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(SocialMediaResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSocialMediaRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var metadata = request.Metadata.HasValue
            ? JsonDocument.Parse(request.Metadata.Value.GetRawText())
            : null;

        var command = new UpdateSocialMediaCommand(id, userId, request.Type, metadata);
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(new DeleteSocialMediaCommand(id, userId), cancellationToken);
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

public sealed record CreateSocialMediaRequest(string Type, JsonElement? Metadata);

public sealed record UpdateSocialMediaRequest(string Type, JsonElement? Metadata);
