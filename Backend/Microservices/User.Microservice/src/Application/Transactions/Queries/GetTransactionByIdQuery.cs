using Application.Abstractions.Data;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Transactions.Queries;

public sealed record GetTransactionByIdQuery(Guid Id, bool IncludeDeleted) : IRequest<Result<Transaction>>;

public sealed class GetTransactionByIdQueryHandler
    : IRequestHandler<GetTransactionByIdQuery, Result<Transaction>>
{
    private readonly IRepository<Transaction> _repository;

    public GetTransactionByIdQueryHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<Transaction>();
    }

    public async Task<Result<Transaction>> Handle(
        GetTransactionByIdQuery request,
        CancellationToken cancellationToken)
    {
        var query = _repository.GetAll()
            .AsNoTracking()
            .Where(item => item.Id == request.Id);

        if (!request.IncludeDeleted)
        {
            query = query.Where(item => !item.IsDeleted);
        }

        var transaction = await query.FirstOrDefaultAsync(cancellationToken);
        if (transaction == null)
        {
            return Result.Failure<Transaction>(
                new Error("Transaction.NotFound", "Transaction not found."));
        }

        return Result.Success(transaction);
    }
}
