using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Posts.Commands;
using Application.Posts.Models;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Attributes;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace WebApi.Controllers;

[ApiController]
[Route("api/AiGeneration")]
[Authorize]
public sealed class AiGenerationController : ApiController
{
    public AiGenerationController(IMediator mediator) : base(mediator)
    {
    }

    [HttpPost("post-prepare")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Result<PrepareGeminiPostsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PreparePosts(
        [FromBody] PrepareGeminiPostsRequest? request,
        CancellationToken cancellationToken)
    {
        return await PreparePostsInternal(request, cancellationToken);
    }

    [HttpPost("post/prepare")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> PreparePostsAlias(
        [FromBody] PrepareGeminiPostsRequest? request,
        CancellationToken cancellationToken)
    {
        return await PreparePostsInternal(request, cancellationToken);
    }

    private async Task<IActionResult> PreparePostsInternal(
        PrepareGeminiPostsRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var requestResult = ParsePrepareGeminiPostsRequest(request);
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

    [HttpPost("captions")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Result<GenerateSocialMediaCaptionsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GenerateSocialMediaCaptions(
        [FromBody] GenerateSocialMediaCaptionsRequest? request,
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

        var requestResult = ParseGenerateSocialMediaCaptionsRequest(request);
        if (requestResult.IsFailure)
        {
            return HandleFailure(Result.Failure<GenerateSocialMediaCaptionsResponse>(requestResult.Error));
        }

        var result = await _mediator.Send(
            new GenerateSocialMediaCaptionsCommand(
                userId,
                requestResult.Value.SocialMedia,
                requestResult.Value.Language,
                requestResult.Value.Instruction),
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

    private static Result<PrepareGeminiPostsRequestPayload> ParsePrepareGeminiPostsRequest(
        PrepareGeminiPostsRequest? request)
    {
        if (request is null)
        {
            return Result.Failure<PrepareGeminiPostsRequestPayload>(
                new Error("Gemini.InvalidRequest", "Request body is required."));
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

    private static Result<GenerateSocialMediaCaptionsRequestPayload> ParseGenerateSocialMediaCaptionsRequest(
        GenerateSocialMediaCaptionsRequest? request)
    {
        if (request is null)
        {
            return Result.Failure<GenerateSocialMediaCaptionsRequestPayload>(
                new Error("Gemini.InvalidRequest", "Request body is required."));
        }

        if (request.SocialMedia is null)
        {
            return Result.Failure<GenerateSocialMediaCaptionsRequestPayload>(
                new Error("SocialMedia.InvalidRequest", "socialMedia must be a JSON array."));
        }

        var socialMedia = new List<SocialMediaCaptionPostInput>();
        foreach (var item in request.SocialMedia)
        {
            if (item is null)
            {
                continue;
            }

            var platform = item.ResolvePlatform();
            if (string.IsNullOrWhiteSpace(platform))
            {
                return Result.Failure<GenerateSocialMediaCaptionsRequestPayload>(
                    new Error("SocialMedia.InvalidRequest", "Each social media item must include a platform."));
            }

            var resourceIdsResult = item.ResolveResourceIds();
            if (resourceIdsResult.IsFailure)
            {
                return Result.Failure<GenerateSocialMediaCaptionsRequestPayload>(resourceIdsResult.Error);
            }

            socialMedia.Add(new SocialMediaCaptionPostInput(
                item.PostId ?? Guid.Empty,
                platform,
                resourceIdsResult.Value));
        }

        if (socialMedia.Count == 0)
        {
            return Result.Failure<GenerateSocialMediaCaptionsRequestPayload>(
                new Error("SocialMedia.InvalidRequest", "socialMedia must contain at least one item."));
        }

        return Result.Success(new GenerateSocialMediaCaptionsRequestPayload(
            socialMedia,
            request.Language,
            request.Instruction));
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
    public string? Language { get; set; }
    public string? Instruction { get; set; }
    public IReadOnlyList<GenerateSocialMediaCaptionPostRequest>? SocialMedia { get; set; }
}

public sealed class GenerateSocialMediaCaptionPostRequest
{
    public Guid? PostId { get; set; }
    public string? Platform { get; set; }
    public IReadOnlyList<Guid>? ResourceIds { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public string? ResolvePlatform()
    {
        if (!string.IsNullOrWhiteSpace(Platform))
        {
            return Platform;
        }

        if (TryResolveStringFromExtensionData(out var aliasPlatform, "socialMediaType", "type"))
        {
            return aliasPlatform;
        }

        return null;
    }

    public Result<IReadOnlyList<Guid>> ResolveResourceIds()
    {
        if (TryNormalizeGuidList(ResourceIds, out var directResourceIds))
        {
            return Result.Success<IReadOnlyList<Guid>>(directResourceIds);
        }

        if (TryResolveGuidListFromExtensionData(out var extensionResult, "resourceList", "resources", "resource list"))
        {
            return extensionResult;
        }

        return Result.Success<IReadOnlyList<Guid>>(Array.Empty<Guid>());
    }

    private bool TryResolveStringFromExtensionData(
        out string? value,
        params string[] propertyNames)
    {
        value = null;

        if (ExtensionData is null || ExtensionData.Count == 0)
        {
            return false;
        }

        foreach (var propertyName in propertyNames)
        {
            if (TryResolveStringFromExtensionData(propertyName, out value))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryResolveStringFromExtensionData(
        string propertyName,
        out string? value)
    {
        value = null;

        if (ExtensionData is null || ExtensionData.Count == 0)
        {
            return false;
        }

        var normalizedTarget = NormalizePropertyName(propertyName);
        foreach (var pair in ExtensionData)
        {
            if (NormalizePropertyName(pair.Key) != normalizedTarget ||
                pair.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var raw = pair.Value.GetString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            value = raw;
            return true;
        }

        return false;
    }

    private bool TryResolveGuidListFromExtensionData(
        out Result<IReadOnlyList<Guid>> result,
        params string[] propertyNames)
    {
        result = Result.Success<IReadOnlyList<Guid>>(Array.Empty<Guid>());

        if (ExtensionData is null || ExtensionData.Count == 0)
        {
            return false;
        }

        foreach (var propertyName in propertyNames)
        {
            if (TryResolveGuidListFromExtensionData(propertyName, out result))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryResolveGuidListFromExtensionData(
        string propertyName,
        out Result<IReadOnlyList<Guid>> result)
    {
        result = Result.Success<IReadOnlyList<Guid>>(Array.Empty<Guid>());

        if (ExtensionData is null || ExtensionData.Count == 0)
        {
            return false;
        }

        var normalizedTarget = NormalizePropertyName(propertyName);
        foreach (var pair in ExtensionData)
        {
            if (NormalizePropertyName(pair.Key) != normalizedTarget)
            {
                continue;
            }

            if (pair.Value.ValueKind != JsonValueKind.Array)
            {
                result = Result.Failure<IReadOnlyList<Guid>>(
                    new Error("Resource.InvalidRequest", $"{propertyName} must be an array of GUID values."));
                return true;
            }

            var parsed = new List<Guid>();
            foreach (var element in pair.Value.EnumerateArray())
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
                    result = Result.Failure<IReadOnlyList<Guid>>(
                        new Error("Resource.InvalidRequest", $"{propertyName} must contain valid GUID values."));
                    return true;
                }

                if (!parsed.Contains(resourceId))
                {
                    parsed.Add(resourceId);
                }
            }

            result = Result.Success<IReadOnlyList<Guid>>(parsed);
            return true;
        }

        return false;
    }

    private static bool TryNormalizeGuidList(
        IReadOnlyList<Guid>? values,
        out IReadOnlyList<Guid> normalized)
    {
        normalized = Array.Empty<Guid>();

        if (values is null)
        {
            return false;
        }

        normalized = values
            .Where(value => value != Guid.Empty)
            .Distinct()
            .ToList();

        return true;
    }

    private static string NormalizePropertyName(string propertyName)
    {
        var characters = propertyName
            .Where(character => character is not (' ' or '_' or '-'))
            .Select(char.ToLowerInvariant);

        return new string(characters.ToArray());
    }
}

public sealed class PrepareGeminiPostsRequest
{
    public Guid? WorkspaceId { get; set; }
    public IReadOnlyList<Guid>? ResourceIds { get; set; }
    public IReadOnlyList<PrepareGeminiPostSocialMediaRequest>? SocialMedia { get; set; }
}

public sealed class PrepareGeminiPostSocialMediaRequest
{
    public string? Platform { get; set; }
    public string? Type { get; set; }
    public IReadOnlyList<Guid>? ResourceIds { get; set; }

    public string? ResolvePostType() => Type;

    public Result<IReadOnlyList<Guid>> ResolveResourceIds()
    {
        if (TryNormalizeGuidList(ResourceIds, out var directResourceIds))
        {
            return Result.Success<IReadOnlyList<Guid>>(directResourceIds);
        }

        return Result.Success<IReadOnlyList<Guid>>(Array.Empty<Guid>());
    }

    private static bool TryNormalizeGuidList(
        IReadOnlyList<Guid>? values,
        out IReadOnlyList<Guid> normalized)
    {
        normalized = Array.Empty<Guid>();

        if (values is null)
        {
            return false;
        }

        normalized = values
            .Where(value => value != Guid.Empty)
            .Distinct()
            .ToList();

        return true;
    }
}

sealed record PrepareGeminiPostsRequestPayload(
    Guid? WorkspaceId,
    IReadOnlyList<Guid> ResourceIds,
    IReadOnlyList<PrepareGeminiPostSocialMediaInput> SocialMedia);

sealed record GenerateSocialMediaCaptionsRequestPayload(
    IReadOnlyList<SocialMediaCaptionPostInput> SocialMedia,
    string? Language,
    string? Instruction);

public sealed record GeminiPostRequest(
    Guid? WorkspaceId,
    List<Guid>? ResourceIds,
    string? Caption,
    string? PostType,
    string? Language,
    string? Instruction);
