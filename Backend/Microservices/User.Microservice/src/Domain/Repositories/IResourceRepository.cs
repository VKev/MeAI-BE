using Domain.Entities;

namespace Domain.Repositories;

public interface IResourceRepository
{
    Task<IReadOnlyList<Resource>> GetForUserAsync(Guid userId, DateTime? cursorCreatedAt, Guid? cursorId,
        int? limit, CancellationToken cancellationToken = default);
    Task<Resource?> GetByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
    Task AddAsync(Resource resource, CancellationToken cancellationToken = default);
    void Update(Resource resource);
}
