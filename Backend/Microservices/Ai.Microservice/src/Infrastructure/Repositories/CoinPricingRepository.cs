using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class CoinPricingRepository : ICoinPricingRepository
{
    private readonly MyDbContext _dbContext;
    private readonly DbSet<CoinPricingCatalogEntry> _dbSet;

    public CoinPricingRepository(MyDbContext dbContext)
    {
        _dbContext = dbContext;
        _dbSet = dbContext.Set<CoinPricingCatalogEntry>();
    }

    public async Task<IReadOnlyList<CoinPricingCatalogEntry>> GetActiveAsync(CancellationToken cancellationToken)
    {
        return await _dbSet
            .AsNoTracking()
            .Where(e => e.IsActive)
            .OrderBy(e => e.ActionType)
            .ThenBy(e => e.Model)
            .ThenBy(e => e.Variant)
            .ToListAsync(cancellationToken);
    }

    public async Task<CoinPricingCatalogEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbSet.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
    }

    public async Task<CoinPricingCatalogEntry?> ResolveAsync(
        string actionType,
        string model,
        string? variant,
        CancellationToken cancellationToken)
    {
        var normalizedVariant = string.IsNullOrWhiteSpace(variant) ? null : variant.Trim();

        // 1) Exact match: action + model + variant.
        var exact = await _dbSet
            .AsNoTracking()
            .Where(e => e.IsActive
                && e.ActionType == actionType
                && e.Model == model
                && e.Variant == normalizedVariant)
            .FirstOrDefaultAsync(cancellationToken);
        if (exact is not null) return exact;

        // 2) Model default (Variant IS NULL) for the same action + model.
        var modelDefault = await _dbSet
            .AsNoTracking()
            .Where(e => e.IsActive
                && e.ActionType == actionType
                && e.Model == model
                && e.Variant == null)
            .FirstOrDefaultAsync(cancellationToken);
        if (modelDefault is not null) return modelDefault;

        // 3) Action-level wildcard (Model = "*") so brand-new models automatically inherit
        //    a sane default price instead of 400-ing the generation request.
        return await _dbSet
            .AsNoTracking()
            .Where(e => e.IsActive
                && e.ActionType == actionType
                && e.Model == "*"
                && e.Variant == null)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task AddAsync(CoinPricingCatalogEntry entry, CancellationToken cancellationToken)
    {
        return _dbSet.AddAsync(entry, cancellationToken).AsTask();
    }

    public void Update(CoinPricingCatalogEntry entry)
    {
        _dbSet.Update(entry);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}
