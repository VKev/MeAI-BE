using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Billing;
using Application.Abstractions.Configs;
using Application.Billing;
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
    private const string CaptionEstimateOperation = "captions";
    private const string GeminiDraftPostEstimateOperation = "post";
    private const string PostPrepareEstimateOperation = "post_prepare";
    private const string CaptionModel = "openai/gpt-4o";
    private const string DefaultGeminiDraftPostModel = "gpt-5-4";

    private readonly ICoinPricingService _pricingService;
    private readonly IBillingClient _billingClient;
    private readonly IUserConfigService _userConfigService;

    public AiGenerationController(
        IMediator mediator,
        ICoinPricingService pricingService,
        IBillingClient billingClient,
        IUserConfigService userConfigService) : base(mediator)
    {
        _pricingService = pricingService;
        _billingClient = billingClient;
        _userConfigService = userConfigService;
    }

    [HttpPost("estimate")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Result<AiGenerationCoinEstimateResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> EstimateCoin(
        [FromBody] AiGenerationEstimateRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var operationResult = ResolveEstimateOperation(request?.Operation);
        if (operationResult.IsFailure)
        {
            return HandleFailure(Result.Failure<AiGenerationCoinEstimateResponse>(operationResult.Error));
        }

        Result<CoinCostQuote> quoteResult;
        if (operationResult.Value == CaptionEstimateOperation)
        {
            quoteResult = await _pricingService.GetCostAsync(
                CoinActionTypes.CaptionGeneration,
                CaptionModel,
                variant: null,
                quantity: 1,
                cancellationToken);
        }
        else if (operationResult.Value == GeminiDraftPostEstimateOperation)
        {
            var model = await ResolveGeminiDraftPostBillingModelAsync(cancellationToken);
            quoteResult = await _pricingService.GetCostAsync(
                CoinActionTypes.CaptionGeneration,
                model,
                variant: null,
                quantity: 1,
                cancellationToken);
        }
        else
        {
            quoteResult = Result.Success(new CoinCostQuote(
                PostPrepareEstimateOperation,
                "none",
                null,
                "per_request",
                0m,
                1,
                0m));
        }

        if (quoteResult.IsFailure)
        {
            return HandleFailure(Result.Failure<AiGenerationCoinEstimateResponse>(quoteResult.Error));
        }

        var balanceResult = await _billingClient.GetBalanceAsync(userId, cancellationToken);
        if (balanceResult.IsFailure)
        {
            return HandleFailure(Result.Failure<AiGenerationCoinEstimateResponse>(balanceResult.Error));
        }

        var totalCoins = quoteResult.Value.TotalCoins;
        var currentBalance = balanceResult.Value;
        var shortfallCoins = Math.Max(0m, totalCoins - currentBalance);
        var response = new AiGenerationCoinEstimateResponse(
            operationResult.Value,
            quoteResult.Value.ActionType,
            quoteResult.Value.Model,
            quoteResult.Value.Variant,
            quoteResult.Value.Unit,
            quoteResult.Value.UnitCostCoins,
            quoteResult.Value.Quantity,
            totalCoins,
            currentBalance,
            currentBalance >= totalCoins,
            shortfallCoins);

        return Ok(Result.Success(response));
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
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status402PaymentRequired)]
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
                requestResult.Value.Instruction,
                requestResult.Value.MaxTokens,
                requestResult.Value.Style,
                requestResult.Value.WebSearch),
            cancellationToken);

        if (result.IsFailure)
        {
            return MapBillingFailureOrDefault(result);
        }

        return Ok(result);
    }

    [HttpPost("post")]
    [ProducesResponseType(typeof(Result<FacebookDraftPostResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status402PaymentRequired)]
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
            return MapBillingFailureOrDefault(result);
        }

        return Ok(result);
    }

    private static Result<string> ResolveEstimateOperation(string? operation)
    {
        if (string.IsNullOrWhiteSpace(operation))
        {
            return Result.Failure<string>(
                new Error("AiGenerationEstimate.InvalidOperation", "operation is required."));
        }

        var normalized = NormalizePropertyName(operation);
        return normalized switch
        {
            "caption" or "captions" => Result.Success(CaptionEstimateOperation),
            "post" or "geminipost" or "draftpost" => Result.Success(GeminiDraftPostEstimateOperation),
            "postprepare" or "preparepost" or "prepareposts" => Result.Success(PostPrepareEstimateOperation),
            _ => Result.Failure<string>(
                new Error(
                    "AiGenerationEstimate.UnsupportedOperation",
                    "operation must be 'captions', 'post', or 'post-prepare'."))
        };
    }

    private async Task<string> ResolveGeminiDraftPostBillingModelAsync(CancellationToken cancellationToken)
    {
        var result = await _userConfigService.GetActiveConfigAsync(cancellationToken);
        if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.Value?.ChatModel))
        {
            return result.Value.ChatModel.Trim();
        }

        return DefaultGeminiDraftPostModel;
    }

    private IActionResult MapBillingFailureOrDefault(Result result)
    {
        if (string.Equals(result.Error.Code, BillingClientErrors.InsufficientFunds, StringComparison.Ordinal))
        {
            return StatusCode(
                StatusCodes.Status402PaymentRequired,
                new ProblemDetails
                {
                    Status = StatusCodes.Status402PaymentRequired,
                    Type = result.Error.Code,
                    Detail = result.Error.Description
                });
        }

        return HandleFailure(result);
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

        if (request.ContainsExtensionProperty("socialMedia"))
        {
            return Result.Failure<GenerateSocialMediaCaptionsRequestPayload>(
                new Error(
                    "SocialMedia.InvalidRequest",
                    "socialMedia arrays are no longer accepted. Send one postId, platform, resourceIds, and optional maxTokens/webSearch."));
        }

        if (request.PostId is null || request.PostId == Guid.Empty)
        {
            return Result.Failure<GenerateSocialMediaCaptionsRequestPayload>(
                new Error("Post.InvalidRequest", "postId is required."));
        }

        var platform = request.ResolvePlatform();
        if (string.IsNullOrWhiteSpace(platform))
        {
            return Result.Failure<GenerateSocialMediaCaptionsRequestPayload>(
                new Error("SocialMedia.InvalidRequest", "platform is required."));
        }

        var resourceIdsResult = request.ResolveResourceIds();
        if (resourceIdsResult.IsFailure)
        {
            return Result.Failure<GenerateSocialMediaCaptionsRequestPayload>(resourceIdsResult.Error);
        }

        var webSearchResult = request.ResolveWebSearch();
        if (webSearchResult.IsFailure)
        {
            return Result.Failure<GenerateSocialMediaCaptionsRequestPayload>(webSearchResult.Error);
        }

        return Result.Success(new GenerateSocialMediaCaptionsRequestPayload(
            new SocialMediaCaptionPostInput(
                request.PostId.Value,
                platform,
                resourceIdsResult.Value),
            request.Language,
            request.Instruction,
            request.MaxTokens,
            request.Style,
            webSearchResult.Value));
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

public sealed class AiGenerationEstimateRequest
{
    public string? Operation { get; set; }
}

public sealed record AiGenerationCoinEstimateResponse(
    string Operation,
    string ActionType,
    string Model,
    string? Variant,
    string Unit,
    decimal UnitCostCoins,
    int Quantity,
    decimal TotalCoins,
    decimal CurrentBalance,
    bool CanAfford,
    decimal ShortfallCoins);

public sealed class GenerateSocialMediaCaptionsRequest
{
    public string? Language { get; set; }
    public string? Instruction { get; set; }
    public Guid? PostId { get; set; }
    public string? Platform { get; set; }
    public IReadOnlyList<Guid>? ResourceIds { get; set; }
    public int? MaxTokens { get; set; }
    public string? Style { get; set; }
    public bool? WebSearch { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public bool ContainsExtensionProperty(string propertyName)
    {
        if (ExtensionData is null || ExtensionData.Count == 0)
        {
            return false;
        }

        var normalizedTarget = NormalizePropertyName(propertyName);
        return ExtensionData.Keys.Any(key => NormalizePropertyName(key) == normalizedTarget);
    }

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

    public Result<bool> ResolveWebSearch()
    {
        if (WebSearch.HasValue)
        {
            return Result.Success(WebSearch.Value);
        }

        if (TryResolveBooleanFromExtensionData(out var extensionResult, "webSearchEnabled", "web_search", "web search"))
        {
            return extensionResult;
        }

        return Result.Success(false);
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

    private bool TryResolveBooleanFromExtensionData(
        out Result<bool> result,
        params string[] propertyNames)
    {
        result = Result.Success(false);

        if (ExtensionData is null || ExtensionData.Count == 0)
        {
            return false;
        }

        foreach (var propertyName in propertyNames)
        {
            if (TryResolveBooleanFromExtensionData(propertyName, out result))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryResolveBooleanFromExtensionData(
        string propertyName,
        out Result<bool> result)
    {
        result = Result.Success(false);

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

            if (pair.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                result = Result.Success(pair.Value.GetBoolean());
                return true;
            }

            if (pair.Value.ValueKind == JsonValueKind.String &&
                bool.TryParse(pair.Value.GetString(), out var parsed))
            {
                result = Result.Success(parsed);
                return true;
            }

            result = Result.Failure<bool>(
                new Error("Caption.InvalidWebSearch", $"{propertyName} must be true or false."));
            return true;
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
    SocialMediaCaptionPostInput SocialMedia,
    string? Language,
    string? Instruction,
    int? MaxTokens,
    string? Style,
    bool WebSearch);

public sealed record GeminiPostRequest(
    Guid? WorkspaceId,
    List<Guid>? ResourceIds,
    string? Caption,
    string? PostType,
    string? Language,
    string? Instruction);
