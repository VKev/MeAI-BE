using System.Text.Json;
using Application.Admin.Models;
using Application.Abstractions.Kie;
using Application.Billing;
using Domain.Entities;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Admin.Queries;

public sealed record GetAdminAiSpendOverviewQuery(string? Period)
    : IRequest<Result<AdminAiSpendOverviewResponse>>;

public sealed class GetAdminAiSpendOverviewQueryHandler
    : IRequestHandler<GetAdminAiSpendOverviewQuery, Result<AdminAiSpendOverviewResponse>>
{
    private const decimal CoinUsdRate = 0.01m;
    private const string Source = "local_ai_spend_records_with_legacy_chat_estimates";

    private static readonly IReadOnlyList<(string Key, string Label)> ActionOrder =
    [
        (CoinActionTypes.ImageGeneration, "Image generation"),
        (CoinActionTypes.ImageReframeVariant, "Image reframe / variant generation"),
        (CoinActionTypes.VideoGeneration, "Video generation"),
        (CoinActionTypes.CaptionGeneration, "Caption generation")
    ];

    private static readonly IReadOnlyList<(string Key, string Label)> ModelOrder =
    [
        ("nano-banana-pro", "nano-banana-pro"),
        ("ideogram/v3-text-to-image", "ideogram/v3-text-to-image"),
        ("veo3_fast", "veo3_fast"),
        ("veo3", "veo3"),
        ("veo3_quality", "veo3_quality"),
        ("openai/gpt-4o", "openai/gpt-4o / caption model"),
        ("gpt-5-4", "gpt-5-4 / caption model")
    ];

    private readonly IAiSpendRecordRepository _spendRecordRepository;
    private readonly IChatRepository _chatRepository;
    private readonly ICoinPricingService _pricingService;
    private readonly IKieAccountService _kieAccountService;

    public GetAdminAiSpendOverviewQueryHandler(
        IAiSpendRecordRepository spendRecordRepository,
        IChatRepository chatRepository,
        ICoinPricingService pricingService,
        IKieAccountService kieAccountService)
    {
        _spendRecordRepository = spendRecordRepository;
        _chatRepository = chatRepository;
        _pricingService = pricingService;
        _kieAccountService = kieAccountService;
    }

    public async Task<Result<AdminAiSpendOverviewResponse>> Handle(
        GetAdminAiSpendOverviewQuery request,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var periods = BuildPeriods(now);
        var selectedPeriod = NormalizePeriod(request.Period);
        var earliestStart = periods.Min(period => period.StartUtc);

        var records = await _spendRecordRepository.GetCreatedBetweenAsync(
            earliestStart,
            now,
            cancellationToken);

        var usageItems = records.Select(ToUsageItem).ToList();
        var recordedReferences = records
            .Select(record => ReferenceKey(record.ReferenceType, record.ReferenceId))
            .ToHashSet(StringComparer.Ordinal);

        var legacyChats = await _chatRepository.GetCreatedBetweenAsync(
            earliestStart,
            now,
            cancellationToken);
        usageItems.AddRange(await BuildLegacyUsageItemsAsync(
            legacyChats,
            recordedReferences,
            cancellationToken));

        var totals = periods
            .Select(period => BuildPeriodTotal(
                period.Name,
                period.StartUtc,
                period.EndUtc,
                usageItems.Where(item => item.CreatedAt >= period.StartUtc && item.CreatedAt < period.EndUtc)))
            .ToList();

        var selectedRange = periods.First(period => period.Name == selectedPeriod);
        var selectedItems = usageItems
            .Where(item => item.CreatedAt >= selectedRange.StartUtc && item.CreatedAt < selectedRange.EndUtc)
            .ToList();

        return Result.Success(new AdminAiSpendOverviewResponse(
            Source,
            "coins",
            CoinUsdRate,
            selectedPeriod,
            now,
            await BuildExternalProviderCreditsAsync(now, cancellationToken),
            totals,
            BuildBreakdown(selectedItems, item => item.ActionType, ActionOrder),
            BuildBreakdown(selectedItems, item => item.Model, ModelOrder)));
    }

    private async Task<IReadOnlyList<AdminExternalProviderCreditResponse>> BuildExternalProviderCreditsAsync(
        DateTime checkedAtUtc,
        CancellationToken cancellationToken)
    {
        var kieCredit = await _kieAccountService.GetCreditBalanceAsync(cancellationToken);

        return
        [
            new AdminExternalProviderCreditResponse(
                AiSpendProviders.Kie,
                "kie_credits",
                kieCredit.RemainingCredits,
                kieCredit.Success,
                kieCredit.Success ? null : kieCredit.Message,
                checkedAtUtc)
        ];
    }

    private async Task<IReadOnlyList<UsageItem>> BuildLegacyUsageItemsAsync(
        IReadOnlyList<Chat> chats,
        HashSet<string> recordedReferences,
        CancellationToken cancellationToken)
    {
        var items = new List<UsageItem>();
        foreach (var chat in chats)
        {
            if (!chat.CreatedAt.HasValue || string.IsNullOrWhiteSpace(chat.Config))
            {
                continue;
            }

            if (TryParseImageChat(chat, out var image))
            {
                var referenceKey = ReferenceKey(CoinReferenceTypes.ChatImage, chat.Id.ToString());
                if (recordedReferences.Contains(referenceKey))
                {
                    continue;
                }

                var quote = await _pricingService.GetCostAsync(
                    CoinActionTypes.ImageGeneration,
                    image.Model,
                    image.Resolution,
                    Math.Max(1, image.ExpectedResultCount),
                    cancellationToken);
                if (quote.IsFailure)
                {
                    continue;
                }

                var refunded = IsFailed(chat.Status);
                items.Add(new UsageItem(
                    chat.CreatedAt.Value,
                    CoinActionTypes.ImageGeneration,
                    image.Model,
                    1,
                    quote.Value.UnitCostCoins,
                    refunded ? quote.Value.UnitCostCoins : 0m));

                var variantQuantity = image.ExpectedResultCount - 1;
                if (variantQuantity > 0)
                {
                    var variantCoins = quote.Value.UnitCostCoins * variantQuantity;
                    items.Add(new UsageItem(
                        chat.CreatedAt.Value,
                        CoinActionTypes.ImageReframeVariant,
                        image.Model,
                        variantQuantity,
                        variantCoins,
                        refunded ? variantCoins : 0m));
                }

                continue;
            }

            if (TryParseVideoChat(chat, out var video))
            {
                var referenceKey = ReferenceKey(CoinReferenceTypes.ChatVideo, chat.Id.ToString());
                if (recordedReferences.Contains(referenceKey))
                {
                    continue;
                }

                var quote = await _pricingService.GetCostAsync(
                    CoinActionTypes.VideoGeneration,
                    video.Model,
                    variant: null,
                    quantity: 1,
                    cancellationToken);
                if (quote.IsFailure)
                {
                    continue;
                }

                items.Add(new UsageItem(
                    chat.CreatedAt.Value,
                    CoinActionTypes.VideoGeneration,
                    video.Model,
                    1,
                    quote.Value.TotalCoins,
                    IsFailed(chat.Status) ? quote.Value.TotalCoins : 0m));
            }
        }

        return items;
    }

    private static UsageItem ToUsageItem(AiSpendRecord record)
    {
        var refundedCoins = string.Equals(record.Status, AiSpendStatuses.Refunded, StringComparison.OrdinalIgnoreCase)
            ? record.TotalCoins
            : 0m;

        return new UsageItem(
            record.CreatedAt,
            record.ActionType,
            record.Model,
            record.Quantity,
            record.TotalCoins,
            refundedCoins);
    }

    private static AdminAiSpendPeriodTotalResponse BuildPeriodTotal(
        string period,
        DateTime startUtc,
        DateTime endUtc,
        IEnumerable<UsageItem> items)
    {
        var materialized = items.ToList();
        var grossCoins = materialized.Sum(item => item.GrossCoins);
        var refundedCoins = materialized.Sum(item => item.RefundedCoins);
        var totalCoins = grossCoins - refundedCoins;

        return new AdminAiSpendPeriodTotalResponse(
            period,
            startUtc,
            endUtc,
            totalCoins,
            ToUsd(totalCoins),
            grossCoins,
            refundedCoins);
    }

    private static IReadOnlyList<AdminAiSpendBreakdownResponse> BuildBreakdown(
        IReadOnlyList<UsageItem> items,
        Func<UsageItem, string> keySelector,
        IReadOnlyList<(string Key, string Label)> expectedOrder)
    {
        var groups = items
            .GroupBy(keySelector, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var results = new List<AdminAiSpendBreakdownResponse>();
        foreach (var (key, label) in expectedOrder)
        {
            groups.TryGetValue(key, out var groupItems);
            results.Add(ToBreakdown(key, label, groupItems ?? []));
            groups.Remove(key);
        }

        foreach (var (key, groupItems) in groups.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            results.Add(ToBreakdown(key, key, groupItems));
        }

        return results;
    }

    private static AdminAiSpendBreakdownResponse ToBreakdown(
        string key,
        string label,
        IReadOnlyList<UsageItem> items)
    {
        var grossCoins = items.Sum(item => item.GrossCoins);
        var refundedCoins = items.Sum(item => item.RefundedCoins);
        var totalCoins = grossCoins - refundedCoins;

        return new AdminAiSpendBreakdownResponse(
            key,
            label,
            items.Sum(item => item.Quantity),
            totalCoins,
            ToUsd(totalCoins),
            grossCoins,
            refundedCoins);
    }

    private static IReadOnlyList<PeriodRange> BuildPeriods(DateTime now)
    {
        var todayStart = now.Date;
        var daysSinceMonday = ((int)todayStart.DayOfWeek + 6) % 7;
        var weekStart = todayStart.AddDays(-daysSinceMonday);
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        return
        [
            new PeriodRange("today", todayStart, now),
            new PeriodRange("week", weekStart, now),
            new PeriodRange("month", monthStart, now)
        ];
    }

    private static string NormalizePeriod(string? period)
    {
        if (string.Equals(period, "today", StringComparison.OrdinalIgnoreCase))
        {
            return "today";
        }

        if (string.Equals(period, "week", StringComparison.OrdinalIgnoreCase))
        {
            return "week";
        }

        return "month";
    }

    private static bool TryParseImageChat(Chat chat, out ImageChatSpend image)
    {
        image = default;
        if (!TryReadConfig(chat.Config, out var config) ||
            !HasAny(config, "Resolution", "OutputFormat", "ExpectedResultCount", "NumberOfVariances"))
        {
            return false;
        }

        image = new ImageChatSpend(
            ReadString(config, "Model") ?? "nano-banana-pro",
            ReadString(config, "Resolution") ?? "1K",
            Math.Max(1, ReadInt(config, "ExpectedResultCount") ?? 1));
        return true;
    }

    private static bool TryParseVideoChat(Chat chat, out VideoChatSpend video)
    {
        video = default;
        if (!TryReadConfig(chat.Config, out var config) ||
            !HasAny(config, "EnableTranslation", "Watermark", "Seeds"))
        {
            return false;
        }

        video = new VideoChatSpend(ReadString(config, "Model") ?? "veo3_fast");
        return true;
    }

    private static bool TryReadConfig(
        string? configJson,
        out Dictionary<string, JsonElement> config)
    {
        config = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(configJson))
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(configJson);
            if (parsed is null)
            {
                return false;
            }

            config = new Dictionary<string, JsonElement>(parsed, StringComparer.OrdinalIgnoreCase);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasAny(Dictionary<string, JsonElement> config, params string[] keys)
    {
        return keys.Any(config.ContainsKey);
    }

    private static string? ReadString(Dictionary<string, JsonElement> config, string key)
    {
        if (!config.TryGetValue(key, out var element))
        {
            return null;
        }

        return element.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(element.GetString())
            ? element.GetString()!.Trim()
            : null;
    }

    private static int? ReadInt(Dictionary<string, JsonElement> config, string key)
    {
        if (!config.TryGetValue(key, out var element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value))
        {
            return value;
        }

        return element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out var parsed)
            ? parsed
            : null;
    }

    private static bool IsFailed(string? status)
    {
        return string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase);
    }

    private static decimal ToUsd(decimal coins)
    {
        return decimal.Round(coins * CoinUsdRate, 2, MidpointRounding.AwayFromZero);
    }

    private static string ReferenceKey(string referenceType, string referenceId)
    {
        return $"{referenceType}:{referenceId}";
    }

    private sealed record PeriodRange(string Name, DateTime StartUtc, DateTime EndUtc);

    private readonly record struct UsageItem(
        DateTime CreatedAt,
        string ActionType,
        string Model,
        int Quantity,
        decimal GrossCoins,
        decimal RefundedCoins);

    private readonly record struct ImageChatSpend(string Model, string Resolution, int ExpectedResultCount);

    private readonly record struct VideoChatSpend(string Model);
}
