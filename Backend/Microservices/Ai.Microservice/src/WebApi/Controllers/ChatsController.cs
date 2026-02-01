using System.Security.Claims;
using Application.Chats.Commands;
using Application.Chats.Models;
using Application.Chats.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Attributes;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace WebApi.Controllers;

[ApiController]
[Route("api/Ai/chats")]
[Authorize]
public sealed class ChatsController : ApiController
{
    public ChatsController(IMediator mediator) : base(mediator)
    {
    }

    [HttpGet("session/{chatSessionId:guid}")]
    [ProducesResponseType(typeof(Result<IEnumerable<ChatResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetBySession(
        Guid chatSessionId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var result = await _mediator.Send(new GetChatsBySessionIdQuery(chatSessionId, userId), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpGet("{chatId:guid}")]
    [ProducesResponseType(typeof(Result<ChatResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetById(
        Guid chatId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var result = await _mediator.Send(new GetChatByIdQuery(chatId, userId), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(Result<ChatResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateChat(
        [FromBody] CreateChatRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var command = new CreateChatCommand(
            userId,
            request.ChatSessionId,
            request.Prompt,
            request.Config,
            request.ReferenceResourceIds);

        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPut("{chatId:guid}")]
    [ProducesResponseType(typeof(Result<ChatResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateChat(
        Guid chatId,
        [FromBody] UpdateChatRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var command = new UpdateChatCommand(
            chatId,
            userId,
            request.Prompt,
            request.Config,
            request.ReferenceResourceIds);

        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpDelete("{chatId:guid}")]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteChat(
        Guid chatId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var result = await _mediator.Send(new DeleteChatCommand(chatId, userId), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPost("video")]
    [ProducesResponseType(typeof(Result<ChatVideoResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateChatVideo(
        [FromBody] CreateChatVideoRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var command = new CreateChatVideoCommand(
            UserId: userId,
            ChatSessionId: request.ChatSessionId,
            Prompt: request.Prompt,
            ResourceIds: request.ResourceIds ?? new List<Guid>(),
            Model: request.Model,
            AspectRatio: request.AspectRatio,
            Seeds: request.Seeds,
            EnableTranslation: request.EnableTranslation,
            Watermark: request.Watermark);

        var result = await _mediator.Send(command, cancellationToken);

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

public sealed record CreateChatVideoRequest(
    Guid ChatSessionId,
    string Prompt,
    List<Guid>? ResourceIds,
    string? Model,
    string? AspectRatio,
    int? Seeds,
    bool? EnableTranslation,
    string? Watermark);

public sealed record CreateChatRequest(
    Guid ChatSessionId,
    string? Prompt,
    string? Config,
    List<Guid>? ReferenceResourceIds);

public sealed record UpdateChatRequest(
    string? Prompt,
    string? Config,
    List<Guid>? ReferenceResourceIds);
