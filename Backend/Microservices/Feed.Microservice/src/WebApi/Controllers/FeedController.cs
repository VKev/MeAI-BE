using System.Security.Claims;
using Application.Comments.Commands;
using Application.Comments.Models;
using Application.Comments.Queries;
using Application.Common;
using Application.Follows.Commands;
using Application.Follows.Models;
using Application.Follows.Queries;
using Application.Posts.Commands;
using Application.Posts.Models;
using Application.Posts.Queries;
using Application.Profiles.Models;
using Application.Profiles.Queries;
using Application.Reports.Commands;
using Application.Reports.Models;
using Application.Reports.Queries;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Attributes;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace WebApi.Controllers;

[ApiController]
[Route("api/Feed")]
[Authorize]
public sealed class FeedController : ApiController
{
    public FeedController(IMediator mediator)
        : base(mediator)
    {
    }

    [Tags("Profiles")]
    [HttpGet("profiles/{username}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result<PublicProfileResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPublicProfileByUsername(string username, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPublicProfileByUsernameQuery(username), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [Tags("Profiles")]
    [HttpGet("profiles/{username}/posts")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result<IReadOnlyList<PostResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPostsByUsername(
        string username,
        [FromQuery] DateTime? cursorCreatedAt,
        [FromQuery] Guid? cursorId,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        Guid? requestingUserId = TryGetUserId(out var userId) ? userId : null;

        var result = await _mediator.Send(
            new GetPostsByUsernameQuery(username, cursorCreatedAt, cursorId, limit, requestingUserId),
            cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [Tags("Posts")]
    [HttpPost("posts")]
    [ProducesResponseType(typeof(Result<PostResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreatePost([FromBody] CreatePostRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(
            new CreatePostCommand(userId, request.Content, request.ResourceIds, request.MediaType),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [Tags("Posts")]
    [HttpGet("posts/feed")]
    [ProducesResponseType(typeof(Result<IReadOnlyList<PostResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetFeed(
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
            new GetFeedPostsQuery(userId, cursorCreatedAt, cursorId, limit),
            cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [Tags("Posts")]
    [HttpGet("posts/{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result<PostResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPostById(Guid id, CancellationToken cancellationToken)
    {
        Guid? requestingUserId = TryGetUserId(out var userId) ? userId : null;

        var result = await _mediator.Send(new GetPostByIdQuery(id, requestingUserId), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [Tags("Posts")]
    [HttpPost("posts/{id:guid}/like")]
    [ProducesResponseType(typeof(Result<PostLikeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> LikePost(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(new LikePostCommand(userId, id), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [Tags("Posts")]
    [HttpDelete("posts/{id:guid}/like")]
    [ProducesResponseType(typeof(Result<PostLikeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UnlikePost(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(new UnlikePostCommand(userId, id), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [Tags("Posts")]
    [HttpDelete("posts/{id:guid}")]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeletePost(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(new DeletePostCommand(userId, id), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [Tags("Comments")]
    [HttpPost("comments")]
    [ProducesResponseType(typeof(Result<CommentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateComment([FromBody] CreateCommentRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(new CreateCommentCommand(userId, request.PostId, request.Content), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [Tags("Comments")]
    [HttpGet("posts/{id:guid}/comments")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result<IReadOnlyList<CommentResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetComments(
        Guid id,
        [FromQuery] DateTime? cursorCreatedAt,
        [FromQuery] Guid? cursorId,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        Guid? requestingUserId = TryGetUserId(out var userId) ? userId : null;

        var result = await _mediator.Send(
            new GetCommentsByPostIdQuery(id, cursorCreatedAt, cursorId, limit, requestingUserId),
            cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [Tags("Comments")]
    [HttpGet("comments/{id:guid}/replies")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result<IReadOnlyList<CommentResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetCommentReplies(
        Guid id,
        [FromQuery] DateTime? cursorCreatedAt,
        [FromQuery] Guid? cursorId,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        Guid? requestingUserId = TryGetUserId(out var userId) ? userId : null;

        var result = await _mediator.Send(
            new GetCommentRepliesQuery(id, cursorCreatedAt, cursorId, limit, requestingUserId),
            cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [Tags("Comments")]
    [HttpDelete("comments/{id:guid}")]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteComment(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(new DeleteCommentCommand(userId, id), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [Tags("Comments")]
    [HttpPost("comments/{id:guid}/reply")]
    [ProducesResponseType(typeof(Result<CommentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ReplyToComment(Guid id, [FromBody] ReplyToCommentRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(new ReplyToCommentCommand(userId, id, request.Content), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [Tags("Follows")]
    [HttpPost("follow/{userId:guid}")]
    [ProducesResponseType(typeof(Result<FollowUserResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Follow(Guid userId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var currentUserId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(new FollowUserCommand(currentUserId, userId), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [Tags("Follows")]
    [HttpDelete("follow/{userId:guid}")]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Unfollow(Guid userId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var currentUserId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(new UnfollowUserCommand(currentUserId, userId), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [Tags("Follows")]
    [HttpGet("followers/{userId:guid}")]
    [ProducesResponseType(typeof(Result<IReadOnlyList<FollowUserResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetFollowers(Guid userId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out _))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(new GetFollowersQuery(userId), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [Tags("Follows")]
    [HttpGet("following/{userId:guid}")]
    [ProducesResponseType(typeof(Result<IReadOnlyList<FollowUserResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetFollowing(Guid userId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out _))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(new GetFollowingQuery(userId), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [Tags("Follows")]
    [HttpGet("follow/suggestions")]
    [ProducesResponseType(typeof(Result<IReadOnlyList<FollowSuggestionResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetFollowSuggestions([FromQuery] int? limit, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var currentUserId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(new GetFollowSuggestionsQuery(currentUserId, limit), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [Tags("Reports")]
    [HttpPost("reports")]
    [ProducesResponseType(typeof(Result<ReportResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateReport([FromBody] CreateReportRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(
            new CreateReportCommand(userId, request.TargetType, request.TargetId, request.Reason),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [Tags("Admin Reports")]
    [HttpGet("admin/reports")]
    [Authorize("ADMIN", "Admin")]
    [ProducesResponseType(typeof(Result<IReadOnlyList<ReportResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAdminReports(
        [FromQuery] string? status,
        [FromQuery] string? targetType,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out _))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(new GetAdminReportsQuery(status, targetType), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [Tags("Admin Reports")]
    [HttpPatch("admin/reports/{id:guid}")]
    [Authorize("ADMIN", "Admin")]
    [ProducesResponseType(typeof(Result<ReportResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ReviewReport(Guid id, [FromBody] ReviewReportRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var adminUserId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(
            new ReviewReportCommand(adminUserId, id, request.Status, request.Action, request.ResolutionNote),
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

public sealed record CreatePostRequest(string? Content, IReadOnlyCollection<Guid>? ResourceIds, string? MediaType);

public sealed record CreateCommentRequest(Guid PostId, string Content);

public sealed record ReplyToCommentRequest(string Content);

public sealed record CreateReportRequest(string TargetType, Guid TargetId, string Reason);

public sealed record ReviewReportRequest(string Status, string? Action, string? ResolutionNote);
