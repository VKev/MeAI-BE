using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class SocialAccountAnalysisSuggestionRepository : ISocialAccountAnalysisSuggestionRepository
{
    private readonly MyDbContext _dbContext;
    private readonly DbSet<SocialAccountAnalysisSuggestion> _dbSet;

    public SocialAccountAnalysisSuggestionRepository(MyDbContext dbContext)
    {
        _dbContext = dbContext;
        _dbSet = dbContext.Set<SocialAccountAnalysisSuggestion>();
    }

    public Task<SocialAccountAnalysisSuggestion?> GetByUserAndSocialMediaAsync(
        Guid userId,
        Guid socialMediaId,
        CancellationToken cancellationToken)
    {
        return _dbSet
            .FirstOrDefaultAsync(
                suggestion => suggestion.UserId == userId && suggestion.SocialMediaId == socialMediaId,
                cancellationToken);
    }

    public Task AddAsync(SocialAccountAnalysisSuggestion entity, CancellationToken cancellationToken)
        => _dbSet.AddAsync(entity, cancellationToken).AsTask();

    public void Update(SocialAccountAnalysisSuggestion entity) => _dbSet.Update(entity);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        => _dbContext.SaveChangesAsync(cancellationToken);
}
