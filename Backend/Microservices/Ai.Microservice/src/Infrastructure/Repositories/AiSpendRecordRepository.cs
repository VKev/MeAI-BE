using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class AiSpendRecordRepository : IAiSpendRecordRepository
{
    private readonly MyDbContext _dbContext;
    private readonly DbSet<AiSpendRecord> _dbSet;

    public AiSpendRecordRepository(MyDbContext dbContext)
    {
        _dbContext = dbContext;
        _dbSet = dbContext.Set<AiSpendRecord>();
    }

    public Task AddAsync(AiSpendRecord entity, CancellationToken cancellationToken)
    {
        return _dbSet.AddAsync(entity, cancellationToken).AsTask();
    }

    public async Task AddRangeAsync(IEnumerable<AiSpendRecord> entities, CancellationToken cancellationToken)
    {
        await _dbSet.AddRangeAsync(entities, cancellationToken);
    }

    public void Update(AiSpendRecord entity)
    {
        _dbSet.Update(entity);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AiSpendRecord>> GetCreatedBetweenAsync(
        DateTime startInclusive,
        DateTime endExclusive,
        CancellationToken cancellationToken)
    {
        return await _dbSet.AsNoTracking()
            .Where(record => record.CreatedAt >= startInclusive && record.CreatedAt < endExclusive)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AiSpendRecord>> GetByReferenceAsync(
        string referenceType,
        string referenceId,
        CancellationToken cancellationToken)
    {
        return await _dbSet
            .Where(record => record.ReferenceType == referenceType && record.ReferenceId == referenceId)
            .ToListAsync(cancellationToken);
    }

    public async Task<AiSpendRecordHistoryPage> GetHistoryAsync(
        AiSpendRecordHistoryQuery query,
        CancellationToken cancellationToken)
    {
        var recordsQuery = _dbSet
            .AsNoTracking()
            .AsQueryable();

        if (query.UserId.HasValue)
        {
            recordsQuery = recordsQuery.Where(record => record.UserId == query.UserId.Value);
        }

        if (query.FromUtc.HasValue)
        {
            recordsQuery = recordsQuery.Where(record => record.CreatedAt >= query.FromUtc.Value);
        }

        if (query.ToUtc.HasValue)
        {
            recordsQuery = recordsQuery.Where(record => record.CreatedAt < query.ToUtc.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.ActionType))
        {
            recordsQuery = recordsQuery.Where(record => record.ActionType == query.ActionType);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var normalizedStatus = query.Status.ToLower();
            recordsQuery = recordsQuery.Where(record => record.Status.ToLower() == normalizedStatus);
        }

        if (query.WorkspaceId.HasValue)
        {
            recordsQuery = recordsQuery.Where(record => record.WorkspaceId == query.WorkspaceId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Provider))
        {
            recordsQuery = recordsQuery.Where(record => record.Provider == query.Provider);
        }

        if (!string.IsNullOrWhiteSpace(query.Model))
        {
            recordsQuery = recordsQuery.Where(record => record.Model == query.Model);
        }

        if (!string.IsNullOrWhiteSpace(query.ReferenceType))
        {
            recordsQuery = recordsQuery.Where(record => record.ReferenceType == query.ReferenceType);
        }

        if (query.CursorCreatedAt.HasValue && query.CursorId.HasValue)
        {
            recordsQuery = recordsQuery.Where(record =>
                record.CreatedAt < query.CursorCreatedAt.Value ||
                (record.CreatedAt == query.CursorCreatedAt.Value && record.Id.CompareTo(query.CursorId.Value) < 0));
        }

        var items = await recordsQuery
            .OrderByDescending(record => record.CreatedAt)
            .ThenByDescending(record => record.Id)
            .Take(query.Limit)
            .ToListAsync(cancellationToken);

        DateTime? nextCursorCreatedAt = null;
        Guid? nextCursorId = null;
        if (items.Count > 0)
        {
            var last = items[^1];
            nextCursorCreatedAt = last.CreatedAt;
            nextCursorId = last.Id;
        }

        return new AiSpendRecordHistoryPage(items, nextCursorCreatedAt, nextCursorId);
    }
}
