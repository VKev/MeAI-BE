using System.Security.Claims;
using Application.Abstractions.TikTok;
using Application.SocialMedias.Models;
using Application.SocialMedias.Commands;
using Application.Users.Models;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Attributes;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace WebApi.Controllers;

[ApiController]
[Route("api/User/tiktok")]
public sealed class TikTokController : ApiController
{
    public TikTokController(IMediator mediator) : base(mediator)
    {
    }

    [HttpGet("authorize")]
    [Authorize]
    [ProducesResponseType(typeof(Result<TikTokOAuthInitiationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Authorize(
        [FromQuery] string? scopes,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(
            new InitiateTikTokOAuthCommand(userId, scopes ?? "user.info.basic"),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpGet("callback")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result<SocialMediaResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery(Name = "error_description")] string? errorDescription,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new CompleteTikTokOAuthCommand(code ?? "", state ?? "", error, errorDescription),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPost("{socialMediaId:guid}/refresh")]
    [Authorize]
    [ProducesResponseType(typeof(Result<SocialMediaResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RefreshToken(
        Guid socialMediaId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(
            new RefreshTikTokTokenCommand(socialMediaId, userId),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPost("{socialMediaId:guid}/publish")]
    [Authorize]
    [ProducesResponseType(typeof(Result<TikTokPublishResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PublishVideo(
        Guid socialMediaId,
        [FromBody] PublishVideoRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(
            new PublishTikTokVideoCommand(
                userId,
                socialMediaId,
                request.Title,
                request.PrivacyLevel,
                request.VideoUrl,
                request.DisableDuet,
                request.DisableComment,
                request.DisableStitch,
                request.VideoCoverTimestampMs),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpGet("{socialMediaId:guid}/publish/{publishId}/status")]
    [Authorize]
    [ProducesResponseType(typeof(Result<TikTokPublishStatusResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPublishStatus(
        Guid socialMediaId,
        string publishId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(
            new GetTikTokPublishStatusQuery(userId, socialMediaId, publishId),
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

public sealed record PublishVideoRequest(
    string Title,
    string PrivacyLevel,
    string VideoUrl,
    bool DisableDuet = false,
    bool DisableComment = false,
    bool DisableStitch = false,
    int? VideoCoverTimestampMs = null
);

