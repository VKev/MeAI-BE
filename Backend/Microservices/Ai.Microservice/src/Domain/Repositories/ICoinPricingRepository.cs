using Domain.Entities;

namespace Domain.Repositories;

public interface ICoinPricingRepository
{
    Task<IReadOnlyList<CoinPricingCatalogEntry>> GetActiveAsync(CancellationToken cancellationToken);

    Task<CoinPricingCatalogEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    // Resolves the best (ActionType, Model, Variant) match. When `variant` is null AND an
    // exact null-variant row exists, returns that. Otherwise tries with the variant first.
    Task<CoinPricingCatalogEntry?> ResolveAsync(
        string actionType,
        string model,
        string? variant,
        CancellationToken cancellationToken);

    Task AddAsync(CoinPricingCatalogEntry entry, CancellationToken cancellationToken);

    void Update(CoinPricingCatalogEntry entry);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
