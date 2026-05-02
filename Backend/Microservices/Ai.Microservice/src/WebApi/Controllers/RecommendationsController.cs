using System.Security.Claims;
using Application.Abstractions.Rag;
using Application.Recommendations.Commands;
using Application.Recommendations.Models;
using Application.Recommendations.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Attributes;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace WebApi.Controllers;

[ApiController]
[Route("api/Ai/recommendations")]
[Authorize]
public sealed class RecommendationsController : ApiController
{
    public RecommendationsController(IMediator mediator) : base(mediator)
    {
    }

    [HttpPost("{socialMediaId:guid}/index")]
    [ProducesResponseType(typeof(Result<IndexSocialAccountPostsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> IndexAccount(
        Guid socialMediaId,
        [FromQuery] int? maxPosts,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var result = await _mediator.Send(
            new IndexSocialAccountPostsCommand(userId, socialMediaId, maxPosts),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPost("{socialMediaId:guid}/query")]
    [ProducesResponseType(typeof(Result<AccountRecommendationsAnswer>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> QueryAccount(
        Guid socialMediaId,
        [FromBody] AccountRecommendationsQueryRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var result = await _mediator.Send(
            new QueryAccountRecommendationsQuery(
                userId,
                socialMediaId,
                request.Query,
                request.TopK),
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
