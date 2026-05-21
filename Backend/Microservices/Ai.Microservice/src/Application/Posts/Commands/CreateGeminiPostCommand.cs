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
using SharedLibrary.Extensions;

namespace Application.Posts.Commands;

public sealed record CreateGeminiPostCommand(
    Guid UserId,
    Guid? WorkspaceId,
    IReadOnlyList<Guid> ResourceIds,
    string? Caption,
    string? PostType,
    string? Language,
    string? Instruction) : IRequest<Result<FacebookDraftPostResponse>>;

public sealed class CreateGeminiPostCommandHandler
    : IRequestHandler<CreateGeminiPostCommand, Result<FacebookDraftPostResponse>>
{
    private readonly IPostRepository _postRepository;
    private readonly IPostBuilderRepository _postBuilderRepository;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IUserConfigService _userConfigService;
    private readonly IUserResourceService _userResourceService;
    private readonly IGeminiCaptionService _geminiCaptionService;
    private readonly ICoinPricingService _pricingService;
    private readonly IBillingClient _billingClient;
    private readonly IAiSpendRecordRepository _aiSpendRecordRepository;

    public CreateGeminiPostCommandHandler(
        IPostRepository postRepository,
        IPostBuilderRepository postBuilderRepository,
        IWorkspaceRepository workspaceRepository,
        IUserConfigService userConfigService,
        IUserResourceService userResourceService,
        IGeminiCaptionService geminiCaptionService,
        ICoinPricingService pricingService,
        IBillingClient billingClient,
        IAiSpendRecordRepository aiSpendRecordRepository)
    {
        _postRepository = postRepository;
        _postBuilderRepository = postBuilderRepository;
        _workspaceRepository = workspaceRepository;
        _userConfigService = userConfigService;
        _userResourceService = userResourceService;
        _geminiCaptionService = geminiCaptionService;
        _pricingService = pricingService;
        _billingClient = billingClient;
        _aiSpendRecordRepository = aiSpendRecordRepository;
    }

    public async Task<Result<FacebookDraftPostResponse>> Handle(
        CreateGeminiPostCommand request,
        CancellationToken cancellationToken)
    {
        var workspaceId = request.WorkspaceId == Guid.Empty ? null : request.WorkspaceId;
        if (workspaceId.HasValue)
        {
            var workspaceExists = await _workspaceRepository.ExistsForUserAsync(
                workspaceId.Value,
                request.UserId,
                cancellationToken);

            if (!workspaceExists)
            {
                return Result.Failure<FacebookDraftPostResponse>(PostErrors.WorkspaceNotFound);
            }
        }

        var resolvedPostType = GeminiDraftPostHelper.NormalizePostType(request.PostType);
        if (!GeminiDraftPostHelper.IsSupportedPostType(resolvedPostType))
        {
            return Result.Failure<FacebookDraftPostResponse>(
                new Error("Facebook.InvalidPostType", "Post type must be 'posts' or 'reels'."));
        }

        var resourceIds = request.ResourceIds?
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList() ?? new List<Guid>();

        if (resourceIds.Count == 0)
        {
            return Result.Failure<FacebookDraftPostResponse>(
                new Error("Resource.Missing", "At least one resource is required."));
        }

        var resourcesResult = await _userResourceService.GetPresignedResourcesAsync(
            request.UserId,
            resourceIds,
            cancellationToken);

        if (resourcesResult.IsFailure)
        {
            return Result.Failure<FacebookDraftPostResponse>(resourcesResult.Error);
        }

        var resources = resourcesResult.Value.ToList();

        if (string.Equals(resolvedPostType, "reels", StringComparison.OrdinalIgnoreCase))
        {
            if (resources.Any(IsImageResource))
            {
                return Result.Failure<FacebookDraftPostResponse>(
                    new Error("Facebook.InvalidResource", "Reels do not support image resources."));
            }

            if (resources.Any(resource => !IsVideoResource(resource)))
            {
                return Result.Failure<FacebookDraftPostResponse>(
                    new Error("Facebook.InvalidResource", "Reels require video resources."));
            }
        }

        var caption = request.Caption?.Trim();
        var captionGenerated = false;

        var languageHint = GeminiDraftPostHelper.ResolveLanguageHint(request.Language);
        var activeConfig = await TryGetActiveConfigAsync(cancellationToken);
        var preferredModel = string.IsNullOrWhiteSpace(activeConfig?.ChatModel)
            ? null
            : activeConfig.ChatModel.Trim();
        var billingModel = string.IsNullOrWhiteSpace(preferredModel) ? "gpt-5-4" : preferredModel;

        var quoteResult = await _pricingService.GetCostAsync(
            CoinActionTypes.CaptionGeneration,
            billingModel,
            variant: null,
            quantity: 1,
            cancellationToken);
        if (quoteResult.IsFailure)
        {
            return Result.Failure<FacebookDraftPostResponse>(quoteResult.Error);
        }

        var postId = Guid.CreateVersion7();
        var spendReferenceId = postId.ToString();
        var debitResult = await _billingClient.DebitAsync(
            request.UserId,
            quoteResult.Value.TotalCoins,
            CoinDebitReasons.CaptionGenerationDebit,
            CoinReferenceTypes.GeminiDraftPost,
            spendReferenceId,
            cancellationToken);
        if (debitResult.IsFailure)
        {
            return Result.Failure<FacebookDraftPostResponse>(debitResult.Error);
        }

        var spendRecord = new AiSpendRecord
        {
            Id = Guid.CreateVersion7(),
            UserId = request.UserId,
            WorkspaceId = workspaceId,
            Provider = AiSpendProviders.Kie,
            ActionType = CoinActionTypes.CaptionGeneration,
            Model = billingModel,
            Variant = null,
            Unit = quoteResult.Value.Unit,
            Quantity = quoteResult.Value.Quantity,
            UnitCostCoins = quoteResult.Value.UnitCostCoins,
            TotalCoins = quoteResult.Value.TotalCoins,
            ReferenceType = CoinReferenceTypes.GeminiDraftPost,
            ReferenceId = spendReferenceId,
            Status = AiSpendStatuses.Pending,
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };
        await _aiSpendRecordRepository.AddAsync(spendRecord, cancellationToken);
        await _aiSpendRecordRepository.SaveChangesAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(caption))
        {
            var geminiResources = resources.Select(resource => new GeminiCaptionResource(
                resource.PresignedUrl,
                string.IsNullOrWhiteSpace(resource.ContentType) ? "application/octet-stream" : resource.ContentType))
                .ToList();

            var geminiResult = await _geminiCaptionService.GenerateCaptionAsync(
                new GeminiCaptionRequest(geminiResources, resolvedPostType, languageHint, request.Instruction, preferredModel),
                cancellationToken);

            if (geminiResult.IsFailure)
            {
                await RefundSpendAsync(request.UserId, quoteResult.Value.TotalCoins, spendReferenceId, spendRecord, cancellationToken);
                return Result.Failure<FacebookDraftPostResponse>(geminiResult.Error);
            }

            caption = geminiResult.Value.Trim();
            captionGenerated = true;
        }

        caption ??= string.Empty;
        var titleSource = GeminiDraftPostHelper.NormalizeTitleContent(caption);
        var titleResult = await _geminiCaptionService.GenerateTitleAsync(
            new GeminiTitleRequest(titleSource, languageHint, preferredModel),
            cancellationToken);

        if (titleResult.IsFailure)
        {
            await RefundSpendAsync(request.UserId, quoteResult.Value.TotalCoins, spendReferenceId, spendRecord, cancellationToken);
            return Result.Failure<FacebookDraftPostResponse>(titleResult.Error);
        }

        var hashtags = GeminiDraftPostHelper.ExtractHashtags(caption);
        var hashtagValue = hashtags.Count == 0 ? null : string.Join(' ', hashtags);
        var title = titleResult.Value.Trim();

        var postContent = new PostContent
        {
            Content = caption,
            Hashtag = hashtagValue,
            ResourceList = resourceIds.Select(id => id.ToString()).ToList(),
            PostType = resolvedPostType
        };

        var postBuilder = new PostBuilder
        {
            Id = Guid.CreateVersion7(),
            UserId = request.UserId,
            WorkspaceId = workspaceId,
            OriginKind = PostBuilderOriginKinds.AiGeminiDraft,
            PostType = resolvedPostType,
            ResourceIds = GeminiDraftPostHelper.SerializeResourceIds(resourceIds),
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow,
            UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        var post = new Post
        {
            Id = postId,
            PostBuilderId = postBuilder.Id,
            UserId = request.UserId,
            WorkspaceId = workspaceId,
            Title = string.IsNullOrWhiteSpace(title) ? null : title,
            Content = postContent,
            Status = "draft",
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _postBuilderRepository.AddAsync(postBuilder, cancellationToken);
        await _postRepository.AddAsync(post, cancellationToken);
        await _postRepository.SaveChangesAsync(cancellationToken);

        spendRecord.Status = AiSpendStatuses.Debited;
        spendRecord.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _aiSpendRecordRepository.Update(spendRecord);
        await _aiSpendRecordRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(new FacebookDraftPostResponse(
            post.Id,
            postBuilder.Id,
            post.Status ?? "draft",
            resolvedPostType,
            caption ?? string.Empty,
            resourceIds,
            captionGenerated));
    }

    private async Task RefundSpendAsync(
        Guid userId,
        decimal totalCoins,
        string referenceId,
        AiSpendRecord spendRecord,
        CancellationToken cancellationToken)
    {
        var refund = await _billingClient.RefundAsync(
            userId,
            totalCoins,
            CoinDebitReasons.CaptionGenerationRefund,
            CoinReferenceTypes.GeminiDraftPost,
            referenceId,
            cancellationToken);

        if (refund.IsFailure)
        {
            return;
        }

        spendRecord.Status = AiSpendStatuses.Refunded;
        spendRecord.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _aiSpendRecordRepository.Update(spendRecord);
        await _aiSpendRecordRepository.SaveChangesAsync(cancellationToken);
    }
    private static bool IsImageResource(UserResourcePresignResult resource)
    {
        if (!string.IsNullOrWhiteSpace(resource.ContentType) &&
            resource.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(resource.ResourceType) &&
               resource.ResourceType.Contains("image", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVideoResource(UserResourcePresignResult resource)
    {
        if (!string.IsNullOrWhiteSpace(resource.ContentType) &&
            resource.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(resource.ResourceType) &&
               resource.ResourceType.Contains("video", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<UserAiConfig?> TryGetActiveConfigAsync(CancellationToken cancellationToken)
    {
        var result = await _userConfigService.GetActiveConfigAsync(cancellationToken);
        return result.IsSuccess ? result.Value : null;
    }
}
