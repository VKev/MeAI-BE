using Application.Abstractions.Data;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Transactions.Queries;

public sealed record GetTransactionsQuery(bool IncludeDeleted) : IRequest<Result<List<Transaction>>>;

public sealed class GetTransactionsQueryHandler
    : IRequestHandler<GetTransactionsQuery, Result<List<Transaction>>>
{
    private readonly IRepository<Transaction> _repository;

    public GetTransactionsQueryHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<Transaction>();
    }

    public async Task<Result<List<Transaction>>> Handle(
        GetTransactionsQuery request,
        CancellationToken cancellationToken)
    {
        var query = _repository.GetAll().AsNoTracking();

        if (!request.IncludeDeleted)
        {
            query = query.Where(item => !item.IsDeleted);
        }

        var transactions = await query
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken);

        return Result.Success(transactions);
    }
}
