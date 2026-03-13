using Application.Abstractions.Data;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Transactions.Queries;

public sealed record GetUserTransactionsQuery(Guid UserId, bool IncludeDeleted = false)
    : IRequest<Result<List<Transaction>>>;

public sealed class GetUserTransactionsQueryHandler
    : IRequestHandler<GetUserTransactionsQuery, Result<List<Transaction>>>
{
    private readonly IRepository<Transaction> _repository;

    public GetUserTransactionsQueryHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<Transaction>();
    }

    public async Task<Result<List<Transaction>>> Handle(
        GetUserTransactionsQuery request,
        CancellationToken cancellationToken)
    {
        var query = _repository.GetAll()
            .AsNoTracking()
            .Where(item => item.UserId == request.UserId);

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
