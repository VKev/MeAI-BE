using Domain.Entities;

namespace Application.Usage.Models;

public sealed record AiUsageTiming(
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    int? ProcessingDurationSeconds);

public interface IAiUsageTimingResolver
{
    Task<IReadOnlyDictionary<Guid, AiUsageTiming>> ResolveAsync(
        IReadOnlyList<AiSpendRecord> records,
        CancellationToken cancellationToken);
}
