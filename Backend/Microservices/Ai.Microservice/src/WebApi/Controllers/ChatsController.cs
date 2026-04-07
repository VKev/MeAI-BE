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

    [HttpGet]
    [ProducesResponseType(typeof(Result<IEnumerable<ChatResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var result = await _mediator.Send(new GetUserChatsQuery(userId), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
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

    [HttpGet("workspace/{workspaceId:guid}/resources")]
    [ProducesResponseType(typeof(Result<IEnumerable<WorkspaceAiResourceResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetWorkspaceResources(
        Guid workspaceId,
        [FromQuery] string[]? resourceTypes,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var result = await _mediator.Send(
            new GetWorkspaceAiResourcesQuery(workspaceId, userId, resourceTypes),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpGet("resources")]
    [ProducesResponseType(typeof(Result<IEnumerable<WorkspaceAiResourceResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAllResources(
        [FromQuery] string[]? resourceTypes,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var result = await _mediator.Send(
            new GetAllAiResourcesQuery(userId, resourceTypes),
            cancellationToken);

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
        [FromBody] CreateChatRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        if (request is null)
        {
            return HandleFailure(Result.Failure<ChatResponse>(
                new Error("Chat.InvalidRequest", "Request body is required.")));
        }

        if (!Guid.TryParse(request.ChatSessionId, out var chatSessionId) || chatSessionId == Guid.Empty)
        {
            return HandleFailure(Result.Failure<ChatResponse>(
                new Error("ChatSession.InvalidId", "ChatSessionId must be a valid GUID.")));
        }

        var referenceResourceIdsResult = ParseReferenceResourceIds(request.ReferenceResourceIds);
        if (referenceResourceIdsResult.IsFailure)
        {
            return HandleFailure(Result.Failure<ChatResponse>(referenceResourceIdsResult.Error));
        }

        var command = new CreateChatCommand(
            userId,
            chatSessionId,
            request.Prompt,
            request.Config,
            referenceResourceIdsResult.Value.Count == 0 ? null : referenceResourceIdsResult.Value);

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
        [FromBody] CreateChatVideoRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        if (request is null)
        {
            return HandleFailure(Result.Failure<ChatVideoResponse>(
                new Error("Chat.InvalidRequest", "Request body is required.")));
        }

        if (!Guid.TryParse(request.ChatSessionId, out var chatSessionId) || chatSessionId == Guid.Empty)
        {
            return HandleFailure(Result.Failure<ChatVideoResponse>(
                new Error("ChatSession.InvalidId", "ChatSessionId must be a valid GUID.")));
        }

        var resourceIdsResult = ParseResourceIds(request.ResourceIds);
        if (resourceIdsResult.IsFailure)
        {
            return HandleFailure(Result.Failure<ChatVideoResponse>(resourceIdsResult.Error));
        }

        var command = new CreateChatVideoCommand(
            UserId: userId,
            ChatSessionId: chatSessionId,
            Prompt: request.Prompt ?? string.Empty,
            ResourceIds: resourceIdsResult.Value,
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

    [HttpPost("image")]
    [ProducesResponseType(typeof(Result<ChatImageResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateChatImage(
        [FromBody] CreateChatImageRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        if (request is null)
        {
            return HandleFailure(Result.Failure<ChatImageResponse>(
                new Error("Chat.InvalidRequest", "Request body is required.")));
        }

        if (!Guid.TryParse(request.ChatSessionId, out var chatSessionId) || chatSessionId == Guid.Empty)
        {
            return HandleFailure(Result.Failure<ChatImageResponse>(
                new Error("ChatSession.InvalidId", "ChatSessionId must be a valid GUID.")));
        }

        var resourceIdsResult = ParseResourceIds(request.ResourceIds);
        if (resourceIdsResult.IsFailure)
        {
            return HandleFailure(Result.Failure<ChatImageResponse>(resourceIdsResult.Error));
        }

        var command = new CreateChatImageCommand(
            UserId: userId,
            ChatSessionId: chatSessionId,
            Prompt: request.Prompt ?? string.Empty,
            ResourceIds: resourceIdsResult.Value,
            AspectRatio: request.AspectRatio,
            Resolution: request.Resolution,
            OutputFormat: request.OutputFormat,
            NumberOfVariances: request.NumberOfVariances);

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

    private static Result<List<Guid>> ParseReferenceResourceIds(List<string>? rawIds)
    {
        if (rawIds is null || rawIds.Count == 0)
        {
            return Result.Success(new List<Guid>());
        }

        var parsedIds = new List<Guid>();
        foreach (var rawId in rawIds)
        {
            if (string.IsNullOrWhiteSpace(rawId))
            {
                continue;
            }

            if (!Guid.TryParse(rawId, out var parsedId) || parsedId == Guid.Empty)
            {
                return Result.Failure<List<Guid>>(
                    new Error("Chat.InvalidReferenceResourceIds", "referenceResourceIds must contain valid GUID values."));
            }

            if (!parsedIds.Contains(parsedId))
            {
                parsedIds.Add(parsedId);
            }
        }

        return Result.Success(parsedIds);
    }

    private static Result<List<Guid>> ParseResourceIds(List<string>? rawIds)
    {
        if (rawIds is null || rawIds.Count == 0)
        {
            return Result.Success(new List<Guid>());
        }

        var parsedIds = new List<Guid>();
        foreach (var rawId in rawIds)
        {
            if (string.IsNullOrWhiteSpace(rawId))
            {
                continue;
            }

            if (!Guid.TryParse(rawId, out var parsedId) || parsedId == Guid.Empty)
            {
                return Result.Failure<List<Guid>>(
                    new Error("Chat.InvalidResourceIds", "resourceIds must contain valid GUID values."));
            }

            if (!parsedIds.Contains(parsedId))
            {
                parsedIds.Add(parsedId);
            }
        }

        return Result.Success(parsedIds);
    }
}

public sealed record CreateChatVideoRequest(
    string? ChatSessionId,
    string? Prompt,
    List<string>? ResourceIds,
    string? Model,
    string? AspectRatio,
    int? Seeds,
    bool? EnableTranslation,
    string? Watermark);

public sealed record CreateChatImageRequest(
    string? ChatSessionId,
    string? Prompt,
    List<string>? ResourceIds,
    string? AspectRatio,
    string? Resolution,
    string? OutputFormat,
    int? NumberOfVariances);

public sealed record CreateChatRequest(
    string? ChatSessionId,
    string? Prompt,
    string? Config,
    List<string>? ReferenceResourceIds);

public sealed record UpdateChatRequest(
    string? Prompt,
    string? Config,
    List<Guid>? ReferenceResourceIds);
