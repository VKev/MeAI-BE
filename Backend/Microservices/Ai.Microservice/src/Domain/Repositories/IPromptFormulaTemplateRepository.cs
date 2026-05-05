using Domain.Entities;

namespace Domain.Repositories;

public interface IPromptFormulaTemplateRepository
{
    Task<IReadOnlyList<PromptFormulaTemplate>> GetAllAsync(CancellationToken cancellationToken);

    Task<PromptFormulaTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<PromptFormulaTemplate?> GetByKeyAsync(string key, CancellationToken cancellationToken);

    Task AddAsync(PromptFormulaTemplate entity, CancellationToken cancellationToken);

    void Update(PromptFormulaTemplate entity);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
