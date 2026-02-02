using System.Security.Claims;
using Application.Posts.Commands;
using Application.Posts.Models;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Attributes;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace WebApi.Controllers;

[ApiController]
[Route("api/Gemini")]
[Authorize]
public sealed class GeminiController : ApiController
{
    public GeminiController(IMediator mediator) : base(mediator)
    {
    }

    [HttpPost("post")]
    [ProducesResponseType(typeof(Result<FacebookDraftPostResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreatePost(
        [FromBody] GeminiPostRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var result = await _mediator.Send(
            new CreateGeminiPostCommand(
                userId,
                request.ResourceIds ?? new List<Guid>(),
                request.Caption,
                request.PostType,
                request.Language,
                request.Instruction),
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

public sealed record GeminiPostRequest(
    List<Guid>? ResourceIds,
    string? Caption,
    string? PostType,
    string? Language,
    string? Instruction);
