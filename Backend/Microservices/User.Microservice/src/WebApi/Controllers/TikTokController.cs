using System.Security.Claims;
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

    private bool TryGetUserId(out Guid userId)
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claimValue, out userId);
    }
}
