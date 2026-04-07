using System.Security.Claims;
using System.Text.Json;
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

    [HttpPost("post-prepare")]
    [HttpPost("post/prepare")]
    [ProducesResponseType(typeof(Result<PrepareGeminiPostsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PreparePosts(
        [FromBody] JsonElement payload,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var requestResult = ParsePrepareGeminiPostsRequest(payload);
        if (requestResult.IsFailure)
        {
            return HandleFailure(Result.Failure<PrepareGeminiPostsResponse>(requestResult.Error));
        }

        var result = await _mediator.Send(
            new PrepareGeminiPostsCommand(
                userId,
                requestResult.Value.WorkspaceId,
                requestResult.Value.SocialMedia,
                requestResult.Value.PostType,
                requestResult.Value.Language,
                requestResult.Value.Instruction),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPost("captions")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(Result<GenerateSocialMediaCaptionsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GenerateSocialMediaCaptions(
        [FromForm] GenerateSocialMediaCaptionsRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        if (request is null)
        {
            return HandleFailure(Result.Failure<GenerateSocialMediaCaptionsResponse>(
                new Error("Gemini.InvalidRequest", "Request body is required.")));
        }

        if (request.TemplateResource is null || request.TemplateResource.Length == 0)
        {
            return HandleFailure(Result.Failure<GenerateSocialMediaCaptionsResponse>(
                new Error("Gemini.TemplateResourceMissing", "templateResource file is required.")));
        }

        var socialMediaResult = ParseSocialMediaInputs(request.SocialMedia);
        if (socialMediaResult.IsFailure)
        {
            return HandleFailure(Result.Failure<GenerateSocialMediaCaptionsResponse>(socialMediaResult.Error));
        }

        await using var memoryStream = new MemoryStream();
        await request.TemplateResource.CopyToAsync(memoryStream, cancellationToken);

        var result = await _mediator.Send(
            new GenerateSocialMediaCaptionsCommand(
                userId,
                new GeminiTemplateResourceInput(
                    request.TemplateResource.FileName,
                    request.TemplateResource.ContentType ?? "application/octet-stream",
                    memoryStream.ToArray()),
                socialMediaResult.Value,
                request.Language,
                request.Instruction),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
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
                request.WorkspaceId,
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

    private static Result<PrepareGeminiPostsRequestPayload> ParsePrepareGeminiPostsRequest(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return Result.Failure<PrepareGeminiPostsRequestPayload>(
                new Error("Gemini.InvalidRequest", "Request body must be a JSON object."));
        }

        var workspaceId = TryGetGuidProperty(payload, "workspaceId");
        var postType = GetStringProperty(payload, "postType");
        var language = GetStringProperty(payload, "language");
        var instruction = GetStringProperty(payload, "instruction");

        if (!TryGetProperty(payload, "socialMedia", out var socialMediaElement) ||
            socialMediaElement.ValueKind != JsonValueKind.Array)
        {
            return Result.Failure<PrepareGeminiPostsRequestPayload>(
                new Error("SocialMedia.InvalidRequest", "socialMedia must be a JSON array."));
        }

        var socialMedia = new List<PrepareGeminiPostSocialMediaInput>();
        foreach (var item in socialMediaElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var socialMediaId = TryGetGuidProperty(item, "socialMediaId");
            var type = GetStringProperty(item, "type", "platform");
            var resourceIdsResult = GetGuidList(item, "resourceIds", "resourceList", "resource list", "resources");

            if (resourceIdsResult.IsFailure)
            {
                return Result.Failure<PrepareGeminiPostsRequestPayload>(resourceIdsResult.Error);
            }

            socialMedia.Add(new PrepareGeminiPostSocialMediaInput(
                socialMediaId,
                type,
                resourceIdsResult.Value));
        }

        if (socialMedia.Count == 0)
        {
            return Result.Failure<PrepareGeminiPostsRequestPayload>(
                new Error("SocialMedia.InvalidRequest", "socialMedia must contain at least one item."));
        }

        return Result.Success(new PrepareGeminiPostsRequestPayload(
            workspaceId,
            socialMedia,
            postType,
            language,
            instruction));
    }

    private static Result<List<SocialMediaCaptionPlatformInput>> ParseSocialMediaInputs(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Result.Failure<List<SocialMediaCaptionPlatformInput>>(
                new Error("SocialMedia.InvalidRequest", "socialMedia is required."));
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var socialMediaArray = document.RootElement;

            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                TryGetProperty(document.RootElement, "socialMedia", out var nestedSocialMedia))
            {
                socialMediaArray = nestedSocialMedia;
            }

            if (socialMediaArray.ValueKind != JsonValueKind.Array)
            {
                return Result.Failure<List<SocialMediaCaptionPlatformInput>>(
                    new Error("SocialMedia.InvalidRequest", "socialMedia must be a JSON array."));
            }

            var items = new List<SocialMediaCaptionPlatformInput>();

            foreach (var item in socialMediaArray.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var type = GetStringProperty(item, "type", "platform");
                var resourceList = GetStringList(item, "resourceList", "resource list", "resources");

                items.Add(new SocialMediaCaptionPlatformInput(
                    type ?? string.Empty,
                    resourceList));
            }

            if (items.Count == 0)
            {
                return Result.Failure<List<SocialMediaCaptionPlatformInput>>(
                    new Error("SocialMedia.InvalidRequest", "socialMedia must contain at least one item."));
            }

            return Result.Success(items);
        }
        catch (JsonException ex)
        {
            return Result.Failure<List<SocialMediaCaptionPlatformInput>>(
                new Error("SocialMedia.InvalidJson", $"socialMedia JSON is invalid: {ex.Message}"));
        }
    }

    private static IReadOnlyList<string> GetStringList(JsonElement item, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetProperty(item, propertyName, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Array)
            {
                return value.EnumerateArray()
                    .Select(element => element.ValueKind == JsonValueKind.String
                        ? element.GetString()
                        : element.ToString())
                    .Where(entry => !string.IsNullOrWhiteSpace(entry))
                    .Select(entry => entry!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString()?
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList() ?? new List<string>();
            }
        }

        return Array.Empty<string>();
    }

    private static Result<IReadOnlyList<Guid>> GetGuidList(JsonElement item, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetProperty(item, propertyName, out var value))
            {
                continue;
            }

            if (value.ValueKind != JsonValueKind.Array)
            {
                return Result.Failure<IReadOnlyList<Guid>>(
                    new Error("Resource.InvalidRequest", $"{propertyName} must be an array of GUID values."));
            }

            var parsed = new List<Guid>();
            foreach (var element in value.EnumerateArray())
            {
                var raw = element.ValueKind == JsonValueKind.String
                    ? element.GetString()
                    : element.ToString();

                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                if (!Guid.TryParse(raw, out var resourceId) || resourceId == Guid.Empty)
                {
                    return Result.Failure<IReadOnlyList<Guid>>(
                        new Error("Resource.InvalidRequest", $"{propertyName} must contain valid GUID values."));
                }

                if (!parsed.Contains(resourceId))
                {
                    parsed.Add(resourceId);
                }
            }

            return Result.Success<IReadOnlyList<Guid>>(parsed);
        }

        return Result.Success<IReadOnlyList<Guid>>(Array.Empty<Guid>());
    }

    private static string? GetStringProperty(JsonElement item, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (TryGetProperty(item, propertyName, out var value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static Guid? TryGetGuidProperty(JsonElement item, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetProperty(item, propertyName, out var value) ||
                value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            if (Guid.TryParse(value.GetString(), out var parsed) && parsed != Guid.Empty)
            {
                return parsed;
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

public sealed class GenerateSocialMediaCaptionsRequest
{
    public IFormFile? TemplateResource { get; set; }
    public string? SocialMedia { get; set; }
    public string? Language { get; set; }
    public string? Instruction { get; set; }
}

sealed record PrepareGeminiPostsRequestPayload(
    Guid? WorkspaceId,
    IReadOnlyList<PrepareGeminiPostSocialMediaInput> SocialMedia,
    string? PostType,
    string? Language,
    string? Instruction);

public sealed record GeminiPostRequest(
    Guid? WorkspaceId,
    List<Guid>? ResourceIds,
    string? Caption,
    string? PostType,
    string? Language,
    string? Instruction);
