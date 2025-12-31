using System.Security.Claims;
using System.Text.Json;
using Application.SocialMedias.Models;
using Application.Users.Models;
using Application.WorkspaceSocialMedias.Commands;
using Application.WorkspaceSocialMedias.Queries;
using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Attributes;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace WebApi.Controllers;

[ApiController]
[Route("api/User/workspaces/{workspaceId:guid}/social-medias")]
[Authorize]
public sealed class WorkspaceSocialMediasController : ApiController
{
    private readonly IMapper _mapper;

    public WorkspaceSocialMediasController(IMediator mediator, IMapper mapper)
        : base(mediator)
    {
        _mapper = mapper;
    }

    [HttpGet]
    [ProducesResponseType(typeof(Result<List<SocialMediaResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAll(
        Guid workspaceId,
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
            new GetWorkspaceSocialMediasQuery(workspaceId, userId, cursorCreatedAt, cursorId, limit),
            cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(Result<SocialMediaResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        Guid workspaceId,
        [FromBody] CreateWorkspaceSocialMediaRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var metadata = request.Metadata.HasValue
            ? JsonDocument.Parse(request.Metadata.Value.GetRawText())
            : null;

        var command = _mapper.Map<CreateWorkspaceSocialMediaCommand>(request) with
        {
            WorkspaceId = workspaceId,
            UserId = userId,
            Metadata = metadata
        };
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPut("{socialMediaId:guid}")]
    [ProducesResponseType(typeof(Result<SocialMediaResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(
        Guid workspaceId,
        Guid socialMediaId,
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

        var command = _mapper.Map<UpdateWorkspaceSocialMediaCommand>(request) with
        {
            WorkspaceId = workspaceId,
            SocialMediaId = socialMediaId,
            UserId = userId,
            Metadata = metadata
        };
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpDelete("{socialMediaId:guid}")]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(
        Guid workspaceId,
        Guid socialMediaId,
        CancellationToken cancellationToken)
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

        return Ok(result);
    }

    private bool TryGetUserId(out Guid userId)
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claimValue, out userId);
    }
}

public sealed record CreateWorkspaceSocialMediaRequest(string Type, JsonElement? Metadata);

public sealed record UpdateWorkspaceSocialMediaRequest(string Type, JsonElement? Metadata);
