using Domain.Entities;

namespace Domain.Repositories;

public interface IChatSessionRepository
{
    Task AddAsync(ChatSession entity, CancellationToken cancellationToken);
    void Update(ChatSession entity);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
    Task<ChatSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<ChatSession?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken);
    Task<IEnumerable<ChatSession>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);
    Task<IEnumerable<ChatSession>> GetByWorkspaceIdAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken);
}
