using Domain.Entities;

namespace Domain.Repositories;

public interface IAiSpendRecordRepository
{
    Task AddAsync(AiSpendRecord entity, CancellationToken cancellationToken);

    Task AddRangeAsync(IEnumerable<AiSpendRecord> entities, CancellationToken cancellationToken);

    void Update(AiSpendRecord entity);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<AiSpendRecord>> GetCreatedBetweenAsync(
        DateTime startInclusive,
        DateTime endExclusive,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AiSpendRecord>> GetByReferenceAsync(
        string referenceType,
        string referenceId,
        CancellationToken cancellationToken);

    Task<AiSpendRecordHistoryPage> GetHistoryAsync(
        AiSpendRecordHistoryQuery query,
        CancellationToken cancellationToken);
}
