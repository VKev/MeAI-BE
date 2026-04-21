using Domain.Entities;

namespace Domain.Repositories;

public interface IPostBuilderRepository
{
    Task AddAsync(PostBuilder entity, CancellationToken cancellationToken);
    Task<PostBuilder?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<PostBuilder?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<PostBuilder>> GetByUserAsync(
        Guid userId,
        DateTime? cursorCreatedAt,
        Guid? cursorId,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PostBuilder>> GetByWorkspaceAsync(
        Guid workspaceId,
        Guid userId,
        DateTime? cursorCreatedAt,
        Guid? cursorId,
        int limit,
        CancellationToken cancellationToken);
}
