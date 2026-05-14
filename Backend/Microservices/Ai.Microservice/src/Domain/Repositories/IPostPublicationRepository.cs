using Domain.Entities;

namespace Domain.Repositories;

public interface IPostPublicationRepository
{
    Task AddRangeAsync(IEnumerable<PostPublication> entities, CancellationToken cancellationToken);
    Task<IReadOnlyList<PostPublication>> GetByPostIdsAsync(IReadOnlyList<Guid> postIds, CancellationToken cancellationToken);
    Task<PostPublication?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<PostPublication>> GetByPostIdForUpdateAsync(Guid postId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PostPublication>> GetAllByPostIdIncludingDeletedAsync(Guid postId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PostPublication>> GetBySocialMediaIdForUpdateAsync(
        Guid socialMediaId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<PostPublication>> GetByPostIdsIncludingDeletedForUpdateAsync(
        IReadOnlyList<Guid> postIds,
        CancellationToken cancellationToken);
    Task<PostPublication?> GetBySocialMediaAndExternalContentForUpdateAsync(
        Guid socialMediaId,
        string externalContentId,
        CancellationToken cancellationToken);
    Task<PostPublication?> GetByExternalContentKeyForUpdateAsync(
        string socialMediaType,
        string destinationOwnerId,
        string externalContentId,
        CancellationToken cancellationToken);
    void Update(PostPublication entity);
    void DeleteRange(IEnumerable<PostPublication> entities);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
