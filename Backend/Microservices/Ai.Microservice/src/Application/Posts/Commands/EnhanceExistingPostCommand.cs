using Application.Abstractions.Billing;
using Application.Abstractions.Configs;
using Application.Abstractions.Gemini;
using Application.Abstractions.Resources;
using Application.Billing;
using Application.Posts.Models;
using Domain.Entities;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Posts.Commands;

public sealed record EnhanceExistingPostCommand(
    Guid UserId,
    Guid PostId,
    string Platform,
    IReadOnlyList<Guid>? ResourceIds,
    string? Language,
    string? Instruction,
    int? SuggestionCount) : IRequest<Result<EnhanceExistingPostResponse>>;

public sealed class EnhanceExistingPostCommandHandler
    : IRequestHandler<EnhanceExistingPostCommand, Result<EnhanceExistingPostResponse>>
{
    private const int DefaultSuggestionCount = 3;
    private const int MaxSuggestionCount = 6;
    private const string DefaultCaptionModel = "gpt-5-4";

    private readonly IPostRepository _postRepository;
    private readonly IUserResourceService _userResourceService;
    private readonly IUserConfigService _userConfigService;
    private readonly IGeminiCaptionService _geminiCaptionService;
    private readonly ICoinPricingService _pricingService;
    private readonly IBillingClient _billingClient;
    private readonly IAiSpendRecordRepository _aiSpendRecordRepository;

    public EnhanceExistingPostCommandHandler(
        IPostRepository postRepository,
        IUserResourceService userResourceService,
        IUserConfigService userConfigService,
        IGeminiCaptionService geminiCaptionService,
        ICoinPricingService pricingService,
        IBillingClient billingClient,
        IAiSpendRecordRepository aiSpendRecordRepository)
    {
        _postRepository = postRepository;
        _userResourceService = userResourceService;
        _userConfigService = userConfigService;
        _geminiCaptionService = geminiCaptionService;
        _pricingService = pricingService;
        _billingClient = billingClient;
        _aiSpendRecordRepository = aiSpendRecordRepository;
    }

    public async Task<Result<EnhanceExistingPostResponse>> Handle(
        EnhanceExistingPostCommand request,
        CancellationToken cancellationToken)
    {
        var post = await _postRepository.GetByIdAsync(request.PostId, cancellationToken);
        if (post is null || post.DeletedAt.HasValue)
        {
            return Result.Failure<EnhanceExistingPostResponse>(PostErrors.NotFound);
        }

        if (post.UserId != request.UserId)
        {
            return Result.Failure<EnhanceExistingPostResponse>(PostErrors.Unauthorized);
        }

        var platformResult = NormalizePlatformType(request.Platform);
        if (platformResult.IsFailure)
        {
            return Result.Failure<EnhanceExistingPostResponse>(platformResult.Error);
        }

        var resolvedResourceIds = ResolveResourceIds(request.ResourceIds, post);

        IReadOnlyDictionary<Guid, UserResourcePresignResult> resourcesById = new Dictionary<Guid, UserResourcePresignResult>();
        if (resolvedResourceIds.Count > 0)
        {
            var resourcesResult = await _userResourceService.GetPresignedResourcesAsync(
                request.UserId,
                resolvedResourceIds,
                cancellationToken);

            if (resourcesResult.IsFailure)
            {
                return Result.Failure<EnhanceExistingPostResponse>(resourcesResult.Error);
            }

            resourcesById = resourcesResult.Value.ToDictionary(resource => resource.ResourceId);
        }

        if (!TryResolveResources(resourcesById, resolvedResourceIds, out var orderedResources))
        {
            return Result.Failure<EnhanceExistingPostResponse>(
                new Error("Resource.NotFound", "One or more resources were not found."));
        }

        var activeConfig = await TryGetActiveConfigAsync(cancellationToken);
        var preferredModel = string.IsNullOrWhiteSpace(activeConfig?.ChatModel)
            ? null
            : activeConfig.ChatModel.Trim();
        var suggestionCount = Math.Clamp(
            request.SuggestionCount ?? DefaultSuggestionCount,
            1,
            MaxSuggestionCount);
        var languageHint = ResolveLanguageHint(request.Language);
        var billingModel = preferredModel ?? DefaultCaptionModel;

        var quoteResult = await _pricingService.GetCostAsync(
            CoinActionTypes.PostEnhancement,
            billingModel,
            variant: null,
            quantity: 1,
            cancellationToken);
        if (quoteResult.IsFailure)
        {
            return Result.Failure<EnhanceExistingPostResponse>(quoteResult.Error);
        }

        var referenceId = request.PostId.ToString();
        var debitResult = await _billingClient.DebitAsync(
            request.UserId,
            quoteResult.Value.TotalCoins,
            CoinDebitReasons.PostEnhancementDebit,
            CoinReferenceTypes.PostEnhancement,
            referenceId,
            cancellationToken);
        if (debitResult.IsFailure)
        {
            return Result.Failure<EnhanceExistingPostResponse>(debitResult.Error);
        }

        var spendRecord = new AiSpendRecord
        {
            Id = Guid.CreateVersion7(),
            UserId = request.UserId,
            WorkspaceId = post.WorkspaceId,
            Provider = AiSpendProviders.Kie,
            ActionType = CoinActionTypes.PostEnhancement,
            Model = billingModel,
            Variant = null,
            Unit = quoteResult.Value.Unit,
            Quantity = quoteResult.Value.Quantity,
            UnitCostCoins = quoteResult.Value.UnitCostCoins,
            TotalCoins = quoteResult.Value.TotalCoins,
            ReferenceType = CoinReferenceTypes.PostEnhancement,
            ReferenceId = referenceId,
            Status = AiSpendStatuses.Debited,
            CreatedAt = DateTime.UtcNow
        };

        await _aiSpendRecordRepository.AddAsync(spendRecord, cancellationToken);
        await _aiSpendRecordRepository.SaveChangesAsync(cancellationToken);

        var geminiResources = orderedResources
            .Select(resource => new GeminiCaptionResource(
                resource.PresignedUrl,
                string.IsNullOrWhiteSpace(resource.ContentType)
                    ? "application/octet-stream"
                    : resource.ContentType.Trim()))
            .ToList();

        var postType = GeminiDraftPostHelper.NormalizePostType(post.Content?.PostType);
        var geminiResult = await _geminiCaptionService.GenerateSocialMediaCaptionsAsync(
            new GeminiSocialMediaCaptionRequest(
                geminiResources,
                null,
                platformResult.Value,
                BuildResourceHints(post),
                suggestionCount,
                languageHint,
                request.Instruction,
                preferredModel,
                postType),
            cancellationToken);

        if (geminiResult.IsFailure)
        {
            await RefundPostEnhancementAsync(
                request.UserId,
                quoteResult.Value.TotalCoins,
                referenceId,
                spendRecord,
                cancellationToken);
            return Result.Failure<EnhanceExistingPostResponse>(geminiResult.Error);
        }

        var suggestions = geminiResult.Value
            .Select(caption => new EnhancedPostSuggestionResponse(
                caption.Caption,
                caption.Hashtags,
                caption.TrendingHashtags,
                caption.CallToAction))
            .ToList();

        if (suggestions.Count == 0)
        {
            await RefundPostEnhancementAsync(
                request.UserId,
                quoteResult.Value.TotalCoins,
                referenceId,
                spendRecord,
                cancellationToken);
            return Result.Failure<EnhanceExistingPostResponse>(
                new Error("Gemini.EmptySuggestions", "AI did not return any enhancement suggestions."));
        }

        return Result.Success(new EnhanceExistingPostResponse(
            request.PostId,
            platformResult.Value,
            resolvedResourceIds,
            suggestions[0],
            suggestions.Skip(1).ToList()));
    }

    private async Task RefundPostEnhancementAsync(
        Guid userId,
        decimal totalCoins,
        string referenceId,
        AiSpendRecord spendRecord,
        CancellationToken cancellationToken)
    {
        var refundResult = await _billingClient.RefundAsync(
            userId,
            totalCoins,
            CoinDebitReasons.PostEnhancementRefund,
            CoinReferenceTypes.PostEnhancement,
            referenceId,
            cancellationToken);

        if (refundResult.IsFailure)
        {
            return;
        }

        spendRecord.Status = AiSpendStatuses.Refunded;
        spendRecord.UpdatedAt = DateTime.UtcNow;
        _aiSpendRecordRepository.Update(spendRecord);
        await _aiSpendRecordRepository.SaveChangesAsync(cancellationToken);
    }

    private static IReadOnlyList<Guid> ResolveResourceIds(
        IReadOnlyList<Guid>? requestResourceIds,
        Post post)
    {
        var requestIds = requestResourceIds?
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (requestIds is { Count: > 0 })
        {
            return requestIds;
        }

        if (post.Content?.ResourceList is null || post.Content.ResourceList.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        var fallbackIds = new List<Guid>();
        foreach (var value in post.Content.ResourceList)
        {
            if (Guid.TryParse(value, out var resourceId) && resourceId != Guid.Empty)
            {
                fallbackIds.Add(resourceId);
            }
        }

        return fallbackIds
            .Distinct()
            .ToList();
    }

    private static Result<string> NormalizePlatformType(string? rawType)
    {
        if (string.IsNullOrWhiteSpace(rawType))
        {
            return Result.Failure<string>(
                new Error("SocialMedia.InvalidType", "platform is required."));
        }

        var normalized = rawType.Trim()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        return normalized switch
        {
            "facebook" or "fb" => Result.Success("facebook"),
            "tiktok" => Result.Success("tiktok"),
            "instagram" or "ig" => Result.Success("ig"),
            "threads" => Result.Success("threads"),
            _ => Result.Failure<string>(
                new Error("SocialMedia.InvalidType", "Only Facebook, Instagram, TikTok, and Threads are supported."))
        };
    }

    private static string? ResolveLanguageHint(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var normalized = language.Trim().ToLowerInvariant();
        return normalized switch
        {
            "vi" or "vn" or "vietnamese" => "Vietnamese",
            "en" or "english" => "English",
            _ => language.Trim()
        };
    }

    private async Task<UserAiConfig?> TryGetActiveConfigAsync(CancellationToken cancellationToken)
    {
        var result = await _userConfigService.GetActiveConfigAsync(cancellationToken);
        return result.IsSuccess ? result.Value : null;
    }

    private static bool TryResolveResources(
        IReadOnlyDictionary<Guid, UserResourcePresignResult> resourcesById,
        IReadOnlyList<Guid> resourceIds,
        out List<UserResourcePresignResult> orderedResources)
    {
        orderedResources = new List<UserResourcePresignResult>(resourceIds.Count);
        foreach (var resourceId in resourceIds)
        {
            if (!resourcesById.TryGetValue(resourceId, out var resource))
            {
                return false;
            }

            orderedResources.Add(resource);
        }

        return true;
    }

    private static IReadOnlyList<string> BuildResourceHints(Post post)
    {
        var hints = new List<string>();

        if (!string.IsNullOrWhiteSpace(post.Title))
        {
            hints.Add(post.Title.Trim());
        }

        if (!string.IsNullOrWhiteSpace(post.Content?.Content))
        {
            var content = post.Content.Content.Trim();
            if (content.Length > 140)
            {
                content = content[..140].TrimEnd();
            }

            if (!string.IsNullOrWhiteSpace(content))
            {
                hints.Add(content);
            }
        }

        return hints;
    }
}
