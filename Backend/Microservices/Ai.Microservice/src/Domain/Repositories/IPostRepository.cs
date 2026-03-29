using Domain.Entities;

namespace Domain.Repositories;

public interface IPostRepository
{
    Task AddAsync(Post entity, CancellationToken cancellationToken);
    void Update(Post entity);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
    Task<Post?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Post?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Post>> GetByUserIdAsync(
        Guid userId,
        DateTime? cursorCreatedAt,
        Guid? cursorId,
        int limit,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<Post>> GetByUserIdAndWorkspaceIdAsync(
        Guid userId,
        Guid workspaceId,
        DateTime? cursorCreatedAt,
        Guid? cursorId,
        int limit,
        CancellationToken cancellationToken);
}
