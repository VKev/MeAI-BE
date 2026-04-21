using System.Security.Claims;
using System.Text.Json;
using Application.Abstractions;
using Application.Abstractions.Kie;
using Application.Kie.Commands;
using Application.Kie.Models;
using Application.Kie.Queries;
using Application.Veo.Commands;
using Application.Veo.Models;
using Application.Veo.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Attributes;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace WebApi.Controllers;

[ApiController]
[Route("api/Ai/kie")]
[Authorize]
public sealed class KieController : ApiController
{
    private static readonly JsonSerializerOptions CallbackJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public KieController(IMediator mediator) : base(mediator)
    {
    }

    [HttpGet("image/my-tasks")]
    [ProducesResponseType(typeof(Result<IEnumerable<UserImageTaskResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyImageTasks(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var query = new GetUserImageTasksQuery(userId);
        var result = await _mediator.Send(query, cancellationToken);
        return Ok(result);
    }

    [HttpGet("image/status/{correlationId:guid}")]
    [ProducesResponseType(typeof(Result<ImageTaskStatusResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetImageStatus(
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var query = new GetImageStatusQuery(userId, correlationId);
        var result = await _mediator.Send(query, cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpGet("image/record-info/{correlationId:guid}")]
    [ProducesResponseType(typeof(Result<KieRecordInfoResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetImageRecordInfo(
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var query = new GetImageRecordInfoQuery(userId, correlationId);
        var result = await _mediator.Send(query, cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpGet("video/my-tasks")]
    [ProducesResponseType(typeof(Result<IEnumerable<UserVideoTaskResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyVideoTasks(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var query = new GetUserVideoTasksQuery(userId);
        var result = await _mediator.Send(query, cancellationToken);
        return Ok(result);
    }

    [HttpGet("video/status/{correlationId:guid}")]
    [ProducesResponseType(typeof(Result<VideoTaskStatusResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVideoStatus(
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var query = new GetVideoStatusQuery(userId, correlationId);
        var result = await _mediator.Send(query, cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpGet("video/record-info/{correlationId:guid}")]
    [ProducesResponseType(typeof(Result<VeoRecordInfoResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetVideoRecordInfo(
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var query = new GetVeoRecordInfoQuery(userId, correlationId);
        var result = await _mediator.Send(query, cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpGet("video/get-1080p-video/{correlationId:guid}")]
    [ProducesResponseType(typeof(Result<Veo1080PResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get1080PVideo(
        Guid correlationId,
        [FromQuery] int index = 0,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var query = new Get1080PVideoQuery(userId, correlationId, index);
        var result = await _mediator.Send(query, cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPost("video/refresh-status/{correlationId:guid}")]
    [ProducesResponseType(typeof(Result<RefreshVideoStatusResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RefreshVideoStatus(
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var command = new RefreshVideoStatusCommand(userId, correlationId);
        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPost("callback/{correlationId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> HandleCallback(
        Guid correlationId,
        [FromBody] JsonElement payload,
        [FromQuery] string? provider,
        CancellationToken cancellationToken)
    {
        var resolvedProvider = ResolveProvider(provider, payload);
        if (resolvedProvider is null)
        {
            return HandleFailure(Result.Failure<bool>(
                new Error("Kie.InvalidProvider", "Provider must be 'image' or 'video'.")));
        }

        if (resolvedProvider == "image")
        {
            KieCallbackPayload? imagePayload;
            try
            {
                imagePayload = JsonSerializer.Deserialize<KieCallbackPayload>(payload.GetRawText(), CallbackJsonOptions);
            }
            catch (JsonException)
            {
                imagePayload = null;
            }

            if (imagePayload is null)
            {
                return HandleFailure(Result.Failure<bool>(
                    new Error("Kie.InvalidCallbackPayload", "Invalid image callback payload.")));
            }

            // Flux Kontext callback format: { code, msg, data: { taskId, info: { resultImageUrl } } }.
            // Market format has data.resultJson. If Market parsing missed results, try Flux shape.
            if (string.IsNullOrWhiteSpace(imagePayload.Data?.ResultJson) &&
                payload.ValueKind == JsonValueKind.Object &&
                payload.TryGetProperty("data", out var dataEl) &&
                dataEl.ValueKind == JsonValueKind.Object &&
                dataEl.TryGetProperty("info", out var infoEl) &&
                infoEl.ValueKind == JsonValueKind.Object)
            {
                string? resultUrl = null;
                if (infoEl.TryGetProperty("resultImageUrl", out var resUrlEl) && resUrlEl.ValueKind == JsonValueKind.String)
                {
                    resultUrl = resUrlEl.GetString();
                }

                if (!string.IsNullOrWhiteSpace(resultUrl))
                {
                    var taskId = imagePayload.Data?.TaskId;
                    var isSuccess = imagePayload.Code == 200;
                    var synthesizedResultJson = isSuccess
                        ? JsonSerializer.Serialize(new { resultUrls = new[] { resultUrl } }, CallbackJsonOptions)
                        : null;

                    imagePayload = new KieCallbackPayload(
                        imagePayload.Code,
                        imagePayload.Msg,
                        new KieCallbackData(
                            taskId,
                            isSuccess ? "success" : "fail",
                            synthesizedResultJson,
                            imagePayload.Data?.FailCode,
                            imagePayload.Data?.FailMsg ?? (isSuccess ? null : imagePayload.Msg),
                            imagePayload.Data?.CompleteTime));
                }
            }

            var result = await _mediator.Send(new HandleImageCallbackCommand(correlationId, imagePayload), cancellationToken);
            return Ok(result);
        }

        VeoCallbackPayload? videoPayload;
        try
        {
            videoPayload = JsonSerializer.Deserialize<VeoCallbackPayload>(payload.GetRawText(), CallbackJsonOptions);
        }
        catch (JsonException)
        {
            videoPayload = null;
        }

        // If Veo format parsed but has no result URLs, try Market format (resultJson)
        var hasVeoResults = videoPayload?.Data?.Info?.ResultUrls is { Count: > 0 };
        if (videoPayload is not null && !hasVeoResults)
        {
            KieCallbackPayload? marketPayload = null;
            try
            {
                marketPayload = JsonSerializer.Deserialize<KieCallbackPayload>(payload.GetRawText(), CallbackJsonOptions);
            }
            catch (JsonException) { }

            if (marketPayload?.Data?.ResultJson is not null)
            {
                List<string>? resultUrls = null;
                try
                {
                    var resultJson = JsonSerializer.Deserialize<KieResultJson>(marketPayload.Data.ResultJson, CallbackJsonOptions);
                    resultUrls = resultJson?.ResultUrls;
                }
                catch (JsonException) { }

                videoPayload = new VeoCallbackPayload(
                    marketPayload.Code,
                    marketPayload.Msg,
                    new VeoCallbackData(
                        marketPayload.Data.TaskId,
                        new VeoCallbackInfo(resultUrls, null, null),
                        null));
            }
        }

        if (videoPayload is null)
        {
            return HandleFailure(Result.Failure<bool>(
                new Error("Kie.InvalidCallbackPayload", "Invalid video callback payload.")));
        }

        var videoResult = await _mediator.Send(new HandleVideoCallbackCommand(correlationId, videoPayload), cancellationToken);
        return Ok(videoResult);
    }

    private bool TryGetUserId(out Guid userId)
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claimValue, out userId);
    }

    private static string? ResolveProvider(string? provider, JsonElement payload)
    {
        if (!string.IsNullOrWhiteSpace(provider))
        {
            var normalizedProvider = provider.Trim().ToLowerInvariant();
            if (normalizedProvider is "image" or "video")
            {
                return normalizedProvider;
            }

            return null;
        }

        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("data", out var dataElement) ||
            dataElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (dataElement.TryGetProperty("info", out _))
        {
            return "video";
        }

        if (dataElement.TryGetProperty("state", out _) ||
            dataElement.TryGetProperty("resultJson", out _))
        {
            return "image";
        }

        return null;
    }
}
