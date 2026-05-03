using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class PromptFormulaTemplateRepository : IPromptFormulaTemplateRepository
{
    private readonly MyDbContext _dbContext;
    private readonly DbSet<PromptFormulaTemplate> _dbSet;

    public PromptFormulaTemplateRepository(MyDbContext dbContext)
    {
        _dbContext = dbContext;
        _dbSet = dbContext.Set<PromptFormulaTemplate>();
    }

    public async Task<IReadOnlyList<PromptFormulaTemplate>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await _dbSet
            .AsNoTracking()
            .OrderBy(item => item.Key)
            .ToListAsync(cancellationToken);
    }

    public async Task<PromptFormulaTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbSet.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    public async Task<PromptFormulaTemplate?> GetByKeyAsync(string key, CancellationToken cancellationToken)
    {
        return await _dbSet.FirstOrDefaultAsync(item => item.Key == key, cancellationToken);
    }

    public Task AddAsync(PromptFormulaTemplate entity, CancellationToken cancellationToken)
    {
        return _dbSet.AddAsync(entity, cancellationToken).AsTask();
    }

    public void Update(PromptFormulaTemplate entity)
    {
        _dbSet.Update(entity);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}
