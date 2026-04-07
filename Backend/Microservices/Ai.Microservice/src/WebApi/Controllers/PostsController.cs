using System.Security.Claims;
using Application.Posts.Commands;
using Application.Posts.Models;
using Application.Posts.Queries;
using Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Attributes;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace WebApi.Controllers;

[ApiController]
[Route("api/Ai/posts")]
[Authorize]
public sealed class PostsController : ApiController
{
    public PostsController(IMediator mediator) : base(mediator)
    {
    }

    [HttpGet]
    [ProducesResponseType(typeof(Result<IEnumerable<PostResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAll(
        [FromQuery] DateTime? cursorCreatedAt,
        [FromQuery] Guid? cursorId,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var result = await _mediator.Send(
            new GetUserPostsQuery(userId, cursorCreatedAt, cursorId, limit),
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("workspace/{workspaceId:guid}")]
    [ProducesResponseType(typeof(Result<IEnumerable<PostResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetByWorkspace(
        Guid workspaceId,
        [FromQuery] DateTime? cursorCreatedAt,
        [FromQuery] Guid? cursorId,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var result = await _mediator.Send(
            new GetWorkspacePostsQuery(workspaceId, userId, cursorCreatedAt, cursorId, limit),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpGet("{postId:guid}")]
    [ProducesResponseType(typeof(Result<PostResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetById(Guid postId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var result = await _mediator.Send(new GetPostByIdQuery(postId, userId), cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpGet("social/{socialMediaId:guid}/platform-posts")]
    [ProducesResponseType(typeof(Result<SocialPlatformPostsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPlatformPosts(
        Guid socialMediaId,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var result = await _mediator.Send(
            new GetSocialMediaPlatformPostsQuery(userId, socialMediaId, cursor, limit),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpGet("social/{socialMediaId:guid}/dashboard-summary")]
    [ProducesResponseType(typeof(Result<SocialPlatformDashboardSummaryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetDashboardSummary(
        Guid socialMediaId,
        [FromQuery] int? postLimit,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var result = await _mediator.Send(
            new GetSocialMediaDashboardSummaryQuery(userId, socialMediaId, postLimit),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpGet("social/{socialMediaId:guid}/platform-posts/{platformPostId}/analytics")]
    [ProducesResponseType(typeof(Result<SocialPlatformPostAnalyticsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPlatformPostAnalytics(
        Guid socialMediaId,
        string platformPostId,
        [FromQuery] bool refresh,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var result = await _mediator.Send(
            new GetSocialMediaPlatformPostAnalyticsQuery(userId, socialMediaId, platformPostId, refresh),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(Result<PostResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreatePostRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var command = new CreatePostCommand(
            UserId: userId,
            WorkspaceId: request.WorkspaceId,
            SocialMediaId: request.SocialMediaId,
            Title: request.Title,
            Content: request.Content,
            Status: request.Status);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPut("{postId:guid}")]
    [ProducesResponseType(typeof(Result<PostResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(
        Guid postId,
        [FromBody] UpdatePostRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var command = new UpdatePostCommand(
            PostId: postId,
            UserId: userId,
            WorkspaceId: request.WorkspaceId,
            SocialMediaId: request.SocialMediaId,
            Title: request.Title,
            Content: request.Content,
            Status: request.Status);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpDelete("{postId:guid}")]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(Guid postId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var result = await _mediator.Send(new DeletePostCommand(postId, userId), cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPost("publish")]
    [ProducesResponseType(typeof(Result<PublishPostResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Publish(
        [FromBody] PublishPostRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var command = new PublishPostCommand(
            userId,
            request.PostId,
            request.SocialMediaId,
            request.IsPrivate);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPost("{postId:guid}/check-sensitive")]
    [ProducesResponseType(typeof(Result<CheckSensitiveContentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CheckSensitiveContent(Guid postId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var result = await _mediator.Send(
            new CheckPostSensitiveContentCommand(postId, userId),
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

public sealed record CreatePostRequest(
    Guid? WorkspaceId,
    Guid? SocialMediaId,
    string? Title,
    PostContent? Content,
    string? Status);

public sealed record UpdatePostRequest(
    Guid? WorkspaceId,
    Guid? SocialMediaId,
    string? Title,
    PostContent? Content,
    string? Status);

public sealed record PublishPostRequest(
    Guid PostId,
    Guid SocialMediaId,
    bool? IsPrivate = null);
