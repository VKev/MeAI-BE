using Domain.Entities;

namespace Domain.Repositories;

public interface IFormulaGenerationLogRepository
{
    Task AddAsync(FormulaGenerationLog entity, CancellationToken cancellationToken);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
