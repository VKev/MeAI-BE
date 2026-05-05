using System.Security.Claims;
using Application.Abstractions.Rag;
using Application.Recommendations.Commands;
using Application.Recommendations.Models;
using Application.Recommendations.Queries;
using MediatR;
using Microsoft.AspNetCore.Http;
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

    /// <summary>
    /// Async draft-post generation. Indexes the account's recent posts (skip-if-unchanged),
    /// queries RAG, generates a caption + image grounded in the account's voice and visual style,
    /// uploads the image to S3, and creates a PostBuilder + Post (status="draft"). Returns 202
    /// immediately; final completion is delivered via SignalR notification.
    /// </summary>
    [HttpPost("{socialMediaId:guid}/draft-posts")]
    [ProducesResponseType(typeof(Result<DraftPostTaskResponse>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StartDraftPostGeneration(
        Guid socialMediaId,
        [FromBody] StartDraftPostGenerationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var result = await _mediator.Send(
            new StartDraftPostGenerationCommand(
                UserId: userId,
                SocialMediaId: socialMediaId,
                UserPrompt: request.UserPrompt,
                Style: request.Style,
                WorkspaceId: request.WorkspaceId,
                TopK: request.TopK,
                MaxReferenceImages: request.MaxReferenceImages,
                MaxRagPosts: request.MaxRagPosts),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return StatusCode(StatusCodes.Status202Accepted, result);
    }

    /// <summary>
    /// Status of an async draft-post generation by correlation id. The final result
    /// (post-builder id, resource id, presigned image URL, caption) populates here once done.
    /// </summary>
    [HttpGet("draft-posts/{correlationId:guid}")]
    [ProducesResponseType(typeof(Result<DraftPostTaskResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDraftPostStatus(
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var result = await _mediator.Send(
            new GetDraftPostTaskQuery(userId, correlationId),
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
