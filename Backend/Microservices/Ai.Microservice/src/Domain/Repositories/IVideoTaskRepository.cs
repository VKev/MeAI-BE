using Domain.Entities;

namespace Domain.Repositories;

public interface IVideoTaskRepository
{
    Task AddAsync(VideoTask entity, CancellationToken cancellationToken);

    void Update(VideoTask entity);

    Task<VideoTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<VideoTask?> GetByCorrelationIdAsync(Guid correlationId, CancellationToken cancellationToken);

    Task<VideoTask?> GetByVeoTaskIdAsync(string veoTaskId, CancellationToken cancellationToken);

    Task<IEnumerable<VideoTask>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);
}

