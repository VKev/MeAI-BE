using Domain.Entities;

namespace Domain.Repositories;

public interface ISocialAccountAnalysisSuggestionRepository
{
    Task<SocialAccountAnalysisSuggestion?> GetByUserAndSocialMediaAsync(
        Guid userId,
        Guid socialMediaId,
        CancellationToken cancellationToken);

    Task AddAsync(SocialAccountAnalysisSuggestion entity, CancellationToken cancellationToken);

    void Update(SocialAccountAnalysisSuggestion entity);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
