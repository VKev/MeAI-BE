using Domain.Entities;

namespace Domain.Repositories;

public interface IImageTaskRepository
{
    Task AddAsync(ImageTask entity, CancellationToken cancellationToken);

    void Update(ImageTask entity);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    Task<ImageTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<ImageTask?> GetByCorrelationIdAsync(Guid correlationId, CancellationToken cancellationToken);

    Task<ImageTask?> GetByCorrelationIdForUpdateAsync(Guid correlationId, CancellationToken cancellationToken);

    Task<ImageTask?> GetByKieTaskIdAsync(string kieTaskId, CancellationToken cancellationToken);

    Task<IEnumerable<ImageTask>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);
}
