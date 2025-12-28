using Domain.Entities;

namespace Domain.Repositories;

public interface IWorkspaceRepository
{
    Task<IReadOnlyList<Workspace>> GetForUserAsync(Guid userId, DateTime? cursorCreatedAt, Guid? cursorId,
        int? limit, CancellationToken cancellationToken = default);
    Task<Workspace?> GetByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
    Task AddAsync(Workspace workspace, CancellationToken cancellationToken = default);
    void Update(Workspace workspace);
}
