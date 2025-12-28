using System.Security.Claims;
using System.Text.Json;
using Application.WorkspaceSocialMedias.Commands.CreateWorkspaceSocialMedia;
using Application.WorkspaceSocialMedias.Commands.DeleteWorkspaceSocialMedia;
using Application.WorkspaceSocialMedias.Commands.UpdateWorkspaceSocialMedia;
using Application.WorkspaceSocialMedias.Queries.GetWorkspaceSocialMedias;
using Application.SocialMedias.Contracts;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Attributes;
using SharedLibrary.Common;
using WebApi.Contracts;

namespace WebApi.Controllers;

[Route("Workspaces/{workspaceId:guid}/social-medias")]
[Authorize]
public sealed class WorkspaceSocialMediasController(IMediator mediator) : ApiController(mediator)
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SocialMediaResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAll(Guid workspaceId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(new GetWorkspaceSocialMediasQuery(workspaceId, userId), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    [HttpPost]
    [ProducesResponseType(typeof(SocialMediaResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(Guid workspaceId, [FromBody] CreateWorkspaceSocialMediaRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var metadata = request.Metadata.HasValue
            ? JsonDocument.Parse(request.Metadata.Value.GetRawText())
            : null;

        var command = new CreateWorkspaceSocialMediaCommand(workspaceId, userId, request.Type, metadata);
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    [HttpPut("{socialMediaId:guid}")]
    [ProducesResponseType(typeof(SocialMediaResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(Guid workspaceId, Guid socialMediaId,
        [FromBody] UpdateWorkspaceSocialMediaRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var metadata = request.Metadata.HasValue
            ? JsonDocument.Parse(request.Metadata.Value.GetRawText())
            : null;

        var command = new UpdateWorkspaceSocialMediaCommand(
            workspaceId,
            socialMediaId,
            userId,
            request.Type,
            metadata);
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    [HttpDelete("{socialMediaId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(Guid workspaceId, Guid socialMediaId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(
            new DeleteWorkspaceSocialMediaCommand(workspaceId, socialMediaId, userId),
            cancellationToken);
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

public sealed record CreateWorkspaceSocialMediaRequest(string Type, JsonElement? Metadata);

public sealed record UpdateWorkspaceSocialMediaRequest(string Type, JsonElement? Metadata);
