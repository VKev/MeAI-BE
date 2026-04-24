using System.Text.Json;
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

    [HttpGet("chat-session/{chatSessionId:guid}")]
    [ProducesResponseType(typeof(Result<IEnumerable<PostResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetByChatSession(
        Guid chatSessionId,
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
            new GetChatSessionPostsQuery(chatSessionId, userId, cursorCreatedAt, cursorId, limit),
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

    [HttpPost("dashboard-summary/batch")]
    [ProducesResponseType(typeof(Result<List<SocialPlatformDashboardSummaryResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetBatchDashboardSummary(
        [FromBody] BatchDashboardSummaryRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var result = await _mediator.Send(
            new GetBatchDashboardSummaryQuery(userId, request.SocialMediaIds, request.PostLimit),
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

    [HttpGet("feed/{username}/dashboard-summary")]
    [ProducesResponseType(typeof(Result<SocialPlatformDashboardSummaryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetFeedDashboardSummary(
        string username,
        [FromQuery] int? postLimit,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var result = await _mediator.Send(
            new GetFeedDashboardSummaryQuery(userId, username, postLimit),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpGet("feed/posts/{postId:guid}/analytics")]
    [ProducesResponseType(typeof(Result<SocialPlatformPostAnalyticsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetFeedPostAnalytics(
        Guid postId,
        [FromQuery] int? commentSampleLimit,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var result = await _mediator.Send(
            new GetFeedPostAnalyticsQuery(userId, postId, commentSampleLimit),
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
            ChatSessionId: request.ChatSessionId,
            SocialMediaId: request.SocialMediaId,
            Title: request.Title,
            Content: request.Content,
            Status: request.Status,
            PostBuilderId: request.PostBuilderId,
            Platform: request.Platform);

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
            ChatSessionId: request.ChatSessionId,
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

    [HttpPost("{postId:guid}/schedule")]
    [ProducesResponseType(typeof(Result<PostResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Schedule(
        Guid postId,
        [FromBody] SchedulePostRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var result = await _mediator.Send(
            new SchedulePostCommand(
                postId,
                userId,
                new PostScheduleInput(
                    request.ScheduleGroupId,
                    request.ScheduledAtUtc,
                    request.Timezone,
                    request.SocialMediaIds,
                    request.IsPrivate)),
            cancellationToken);

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
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Result<PublishPostsResponse>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(Result<PublishPostResponse>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Publish(
        [FromBody] JsonElement request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var requestResult = ParsePublishPostsRequest(request);
        if (requestResult.IsFailure)
        {
            return HandleFailure(Result.Failure<PublishPostsResponse>(requestResult.Error));
        }

        var command = new PublishPostsCommand(
            userId,
            requestResult.Value.Targets);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        // Publishing is now async — per-target completion is pushed via SignalR
        // notifications (post.publish.target_completed / target_failed / batch_completed).
        if (requestResult.Value.ReturnSingleResponse)
        {
            return Accepted(Result.Success(result.Value.Posts[0]));
        }

        return Accepted(result);
    }

    [HttpPost("{postId:guid}/unpublish")]
    [ProducesResponseType(typeof(Result<UnpublishPostResponse>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Unpublish(Guid postId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var result = await _mediator.Send(new UnpublishPostCommand(userId, postId), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }
        return Accepted(result);
    }

    [HttpPost("{postId:guid}/update-published")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Result<UpdatePublishedPostResponse>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdatePublished(
        Guid postId,
        [FromBody] UpdatePublishedPostRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }
        if (request is null || string.IsNullOrWhiteSpace(request.Content))
        {
            return HandleFailure(Result.Failure<UpdatePublishedPostResponse>(
                new Error("Post.EmptyContent", "Updated content cannot be empty.")));
        }

        var result = await _mediator.Send(
            new UpdatePublishedPostCommand(userId, postId, request.Content, request.Hashtag),
            cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }
        return Accepted(result);
    }

    public sealed record UpdatePublishedPostRequest(string Content, string? Hashtag);

    [HttpPost("prepare")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Result<PrepareGeminiPostsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Prepare(
        [FromBody] PrepareGeminiPostsRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var requestResult = ParsePreparePostsRequest(request);
        if (requestResult.IsFailure)
        {
            return HandleFailure(Result.Failure<PrepareGeminiPostsResponse>(requestResult.Error));
        }

        var result = await _mediator.Send(
            new PrepareGeminiPostsCommand(
                userId,
                requestResult.Value.WorkspaceId,
                requestResult.Value.ResourceIds,
                requestResult.Value.SocialMedia),
            cancellationToken);

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

    private static Result<PublishPostsRequestPayload> ParsePublishPostsRequest(JsonElement request)
    {
        if (request.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return Result.Failure<PublishPostsRequestPayload>(
                new Error("Post.PublishInvalidRequest", "Request body is required."));
        }

        if (request.ValueKind == JsonValueKind.Array)
        {
            var targetsResult = ParsePublishTargetArray(request);
            if (targetsResult.IsFailure)
            {
                return Result.Failure<PublishPostsRequestPayload>(targetsResult.Error);
            }

            return Result.Success(new PublishPostsRequestPayload(
                targetsResult.Value,
                false));
        }

        if (request.ValueKind != JsonValueKind.Object)
        {
            return Result.Failure<PublishPostsRequestPayload>(
                new Error("Post.PublishInvalidRequest", "Request body must be a JSON object or array."));
        }

        if (TryGetProperty(request, "items", out var itemsElement) ||
            TryGetProperty(request, "targets", out itemsElement) ||
            TryGetProperty(request, "posts", out itemsElement))
        {
            if (itemsElement.ValueKind != JsonValueKind.Array)
            {
                return Result.Failure<PublishPostsRequestPayload>(
                    new Error("Post.PublishInvalidRequest", "items must be a JSON array."));
            }

            var targetsResult = ParsePublishTargetArray(itemsElement);
            if (targetsResult.IsFailure)
            {
                return Result.Failure<PublishPostsRequestPayload>(targetsResult.Error);
            }

            return Result.Success(new PublishPostsRequestPayload(
                targetsResult.Value,
                false));
        }

        var singleTargetResult = ParseSinglePublishTarget(request);
        if (singleTargetResult.IsFailure)
        {
            return Result.Failure<PublishPostsRequestPayload>(singleTargetResult.Error);
        }

        return Result.Success(new PublishPostsRequestPayload(
        [
            singleTargetResult.Value
        ], true));
    }

    private static Result<PrepareGeminiPostsRequestPayload> ParsePreparePostsRequest(
        PrepareGeminiPostsRequest? request)
    {
        if (request is null)
        {
            return Result.Failure<PrepareGeminiPostsRequestPayload>(
                new Error("Post.PrepareInvalidRequest", "Request body is required."));
        }

        if (request.SocialMedia is null)
        {
            return Result.Failure<PrepareGeminiPostsRequestPayload>(
                new Error("SocialMedia.InvalidRequest", "socialMedia must be a JSON array."));
        }

        var builderResourceIds = request.ResourceIds?
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList() ?? new List<Guid>();

        var socialMedia = new List<PrepareGeminiPostSocialMediaInput>();
        foreach (var item in request.SocialMedia)
        {
            if (item is null)
            {
                continue;
            }

            var resourceIdsResult = item.ResolveResourceIds();
            if (resourceIdsResult.IsFailure)
            {
                return Result.Failure<PrepareGeminiPostsRequestPayload>(resourceIdsResult.Error);
            }

            socialMedia.Add(new PrepareGeminiPostSocialMediaInput(
                item.Platform,
                item.ResolvePostType(),
                resourceIdsResult.Value));
        }

        if (socialMedia.Count == 0)
        {
            return Result.Failure<PrepareGeminiPostsRequestPayload>(
                new Error("SocialMedia.InvalidRequest", "socialMedia must contain at least one item."));
        }

        return Result.Success(new PrepareGeminiPostsRequestPayload(
            request.WorkspaceId,
            builderResourceIds,
            socialMedia));
    }

    private static Result<IReadOnlyList<PublishPostTargetInput>> ParsePublishTargetArray(JsonElement arrayElement)
    {
        var targets = new List<PublishPostTargetInput>();

        foreach (var item in arrayElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            if (item.ValueKind != JsonValueKind.Object)
            {
                return Result.Failure<IReadOnlyList<PublishPostTargetInput>>(
                    new Error("Post.PublishInvalidRequest", "Each publish target must be a JSON object."));
            }

            var targetResult = ParseSinglePublishTarget(item);
            if (targetResult.IsFailure)
            {
                return Result.Failure<IReadOnlyList<PublishPostTargetInput>>(targetResult.Error);
            }

            targets.Add(targetResult.Value);
        }

        if (targets.Count == 0)
        {
            return Result.Failure<IReadOnlyList<PublishPostTargetInput>>(
                new Error("Post.PublishMissingTargets", "At least one publish target is required."));
        }

        return Result.Success<IReadOnlyList<PublishPostTargetInput>>(targets);
    }

    private static Result<PublishPostTargetInput> ParseSinglePublishTarget(JsonElement item)
    {
        var postId = GetGuidProperty(item, "postId", "post_id");
        if (!postId.HasValue)
        {
            return Result.Failure<PublishPostTargetInput>(
                new Error("Post.PublishMissingPostId", "Each publish target must include a valid postId."));
        }

        var socialMediaIdsResult = GetGuidListProperty(
            item,
            "socialMediaIds",
            "social_media_ids",
            "socialMediaIdList",
            "social_media_id_list");

        if (socialMediaIdsResult.IsFailure)
        {
            return Result.Failure<PublishPostTargetInput>(socialMediaIdsResult.Error);
        }

        var socialMediaIds = socialMediaIdsResult.Value.ToList();
        if (socialMediaIds.Count == 0)
        {
            var singleSocialMediaId = GetGuidProperty(item, "socialMediaId", "social_media_id");
            if (singleSocialMediaId.HasValue)
            {
                socialMediaIds.Add(singleSocialMediaId.Value);
            }
        }

        if (socialMediaIds.Count == 0)
        {
            return Result.Failure<PublishPostTargetInput>(
                new Error("Post.PublishMissingSocialMedia", "Each publish target must include at least one social media id."));
        }

        return Result.Success(new PublishPostTargetInput(
            postId.Value,
            socialMediaIds,
            GetBooleanProperty(item, "isPrivate", "is_private")));
    }

    private static Result<IReadOnlyList<Guid>> GetGuidListProperty(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetProperty(element, propertyName, out var value))
            {
                continue;
            }

            if (value.ValueKind != JsonValueKind.Array)
            {
                return Result.Failure<IReadOnlyList<Guid>>(
                    new Error("Post.PublishInvalidRequest", $"{propertyName} must be an array of GUID values."));
            }

            var ids = new List<Guid>();
            foreach (var item in value.EnumerateArray())
            {
                var parsedId = item.ValueKind == JsonValueKind.String &&
                               Guid.TryParse(item.GetString(), out var stringId)
                    ? stringId
                    : item.ValueKind == JsonValueKind.Object || item.ValueKind == JsonValueKind.Array
                        ? Guid.Empty
                        : Guid.TryParse(item.ToString(), out var genericId)
                            ? genericId
                            : Guid.Empty;

                if (parsedId == Guid.Empty)
                {
                    return Result.Failure<IReadOnlyList<Guid>>(
                        new Error("Post.PublishInvalidRequest", $"{propertyName} must contain valid GUID values."));
                }

                if (!ids.Contains(parsedId))
                {
                    ids.Add(parsedId);
                }
            }

            return Result.Success<IReadOnlyList<Guid>>(ids);
        }

        return Result.Success<IReadOnlyList<Guid>>(Array.Empty<Guid>());
    }

    private static Guid? GetGuidProperty(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetProperty(element, propertyName, out var value))
            {
                continue;
            }

            var raw = value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : value.ToString();

            if (Guid.TryParse(raw, out var parsedId) && parsedId != Guid.Empty)
            {
                return parsedId;
            }
        }

        return null;
    }

    private static bool? GetBooleanProperty(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetProperty(element, propertyName, out var value))
            {
                continue;
            }

            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return value.GetBoolean();
            }

            if (value.ValueKind == JsonValueKind.String &&
                bool.TryParse(value.GetString(), out var parsedBoolean))
            {
                return parsedBoolean;
            }
        }

        return null;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (NormalizePropertyName(property.Name) == NormalizePropertyName(propertyName))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string NormalizePropertyName(string propertyName)
    {
        var characters = propertyName
            .Where(character => character is not (' ' or '_' or '-'))
            .Select(char.ToLowerInvariant);

        return new string(characters.ToArray());
    }
}

public sealed record CreatePostRequest(
    Guid? WorkspaceId,
    Guid? ChatSessionId,
    Guid? SocialMediaId,
    string? Title,
    PostContent? Content,
    string? Status,
    Guid? PostBuilderId = null,
    string? Platform = null);

public sealed record UpdatePostRequest(
    Guid? WorkspaceId,
    Guid? ChatSessionId,
    Guid? SocialMediaId,
    string? Title,
    PostContent? Content,
    string? Status);

public sealed record SchedulePostRequest(
    Guid? ScheduleGroupId,
    DateTime ScheduledAtUtc,
    string? Timezone,
    IReadOnlyList<Guid>? SocialMediaIds,
    bool? IsPrivate);

sealed record PublishPostsRequestPayload(
    IReadOnlyList<PublishPostTargetInput> Targets,
    bool ReturnSingleResponse);
