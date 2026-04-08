using System.Security.Claims;
using Application.Posts.Models;
using Application.Posts.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Attributes;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace WebApi.Controllers;

[ApiController]
[Route("api/Ai/post-builders")]
[Authorize]
public sealed class PostBuildersController : ApiController
{
    public PostBuildersController(IMediator mediator) : base(mediator)
    {
    }

    [HttpGet("{postBuilderId:guid}")]
    [ProducesResponseType(typeof(Result<PostBuilderDetailsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetById(Guid postBuilderId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var result = await _mediator.Send(
            new GetPostBuilderByIdQuery(postBuilderId, userId),
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
