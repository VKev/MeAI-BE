using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class FormulaGenerationLogRepository : IFormulaGenerationLogRepository
{
    private readonly MyDbContext _dbContext;
    private readonly DbSet<FormulaGenerationLog> _dbSet;

    public FormulaGenerationLogRepository(MyDbContext dbContext)
    {
        _dbContext = dbContext;
        _dbSet = dbContext.Set<FormulaGenerationLog>();
    }

    public Task AddAsync(FormulaGenerationLog entity, CancellationToken cancellationToken)
    {
        return _dbSet.AddAsync(entity, cancellationToken).AsTask();
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}
