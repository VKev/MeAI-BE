using Application.Billing;
using Application.Usage.Models;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Usage.Queries;

public sealed record GetMyAiUsageSummaryQuery(Guid UserId, string? Period, DateTime? FromUtc, DateTime? ToUtc)
    : IRequest<Result<AiUsageSummaryResponse>>;

public sealed class GetMyAiUsageSummaryQueryHandler
    : IRequestHandler<GetMyAiUsageSummaryQuery, Result<AiUsageSummaryResponse>>
{
    private static readonly IReadOnlyList<(string Key, string Label)> ActionOrder =
    [
        (CoinActionTypes.ImageGeneration, "Image generation"),
        (CoinActionTypes.ImageReframeVariant, "Image reframe / variant generation"),
        (CoinActionTypes.VideoGeneration, "Video generation"),
        (CoinActionTypes.CaptionGeneration, "Caption generation"),
        (CoinActionTypes.PostEnhancement, "Post enhancement"),
        (CoinActionTypes.DraftPostGeneration, "Draft post generation"),
        (CoinActionTypes.FormulaGeneration, "Formula generation")
    ];

    private readonly IAiSpendRecordRepository _spendRecordRepository;

    public GetMyAiUsageSummaryQueryHandler(IAiSpendRecordRepository spendRecordRepository)
    {
        _spendRecordRepository = spendRecordRepository;
    }

    public async Task<Result<AiUsageSummaryResponse>> Handle(
        GetMyAiUsageSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var (period, fromUtc, toUtc) = ResolvePeriod(request.Period, request.FromUtc, request.ToUtc, now);

        if (fromUtc >= toUtc)
        {
            return Result.Failure<AiUsageSummaryResponse>(
                new Error("AiUsageSummary.InvalidDateRange", "fromUtc must be earlier than toUtc."));
        }

        var records = await _spendRecordRepository.GetCreatedBetweenAsync(fromUtc, toUtc, cancellationToken);

        var userRecords = records
            .Where(r => r.UserId == request.UserId)
            .ToList();

        var totals = BuildTotals(userRecords);
        var spendByAction = BuildBreakdown(userRecords, r => r.ActionType, ActionOrder);
        var spendByModel = BuildBreakdown(userRecords, r => r.Model, []);

        return Result.Success(new AiUsageSummaryResponse(
            period,
            fromUtc,
            toUtc,
            now,
            totals,
            spendByAction,
            spendByModel));
    }

    private static (string Period, DateTime From, DateTime To) ResolvePeriod(
        string? periodParam,
        DateTime? fromUtc,
        DateTime? toUtc,
        DateTime now)
    {
        if (fromUtc.HasValue && toUtc.HasValue)
        {
            return ("custom", fromUtc.Value, toUtc.Value);
        }

        var todayStart = now.Date;

        if (string.Equals(periodParam, "today", StringComparison.OrdinalIgnoreCase))
        {
            return ("today", todayStart, now);
        }

        if (string.Equals(periodParam, "week", StringComparison.OrdinalIgnoreCase))
        {
            var daysSinceMonday = ((int)todayStart.DayOfWeek + 6) % 7;
            var weekStart = todayStart.AddDays(-daysSinceMonday);
            return ("week", weekStart, now);
        }

        // Default: current calendar month
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return ("month", monthStart, now);
    }

    private static AiUsageSummaryTotals BuildTotals(IReadOnlyList<Domain.Entities.AiSpendRecord> records)
    {
        var grossCoins = records.Sum(r => r.TotalCoins);
        var refundedCoins = records
            .Where(r => string.Equals(r.Status, AiSpendStatuses.Refunded, StringComparison.OrdinalIgnoreCase))
            .Sum(r => r.TotalCoins);
        var netCoins = grossCoins - refundedCoins;

        return new AiUsageSummaryTotals(
            GrossCoins: grossCoins,
            RefundedCoins: refundedCoins,
            NetCoins: netCoins,
            TotalRequests: records.Count);
    }

    private static IReadOnlyList<AiUsageSummaryBreakdownItem> BuildBreakdown(
        IReadOnlyList<Domain.Entities.AiSpendRecord> records,
        Func<Domain.Entities.AiSpendRecord, string> keySelector,
        IReadOnlyList<(string Key, string Label)> expectedOrder)
    {
        var groups = records
            .GroupBy(keySelector, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var results = new List<AiUsageSummaryBreakdownItem>();

        foreach (var (key, label) in expectedOrder)
        {
            groups.TryGetValue(key, out var groupItems);
            results.Add(ToBreakdown(key, label, groupItems ?? []));
            groups.Remove(key);
        }

        foreach (var (key, groupItems) in groups.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            results.Add(ToBreakdown(key, key, groupItems));
        }

        return results;
    }

    private static AiUsageSummaryBreakdownItem ToBreakdown(
        string key,
        string label,
        IReadOnlyList<Domain.Entities.AiSpendRecord> items)
    {
        var grossCoins = items.Sum(r => r.TotalCoins);
        var refundedCoins = items
            .Where(r => string.Equals(r.Status, AiSpendStatuses.Refunded, StringComparison.OrdinalIgnoreCase))
            .Sum(r => r.TotalCoins);

        return new AiUsageSummaryBreakdownItem(
            Key: key,
            Label: label,
            Quantity: items.Sum(r => r.Quantity),
            GrossCoins: grossCoins,
            RefundedCoins: refundedCoins,
            NetCoins: grossCoins - refundedCoins);
    }
}
