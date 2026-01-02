using Application.Abstractions.Data;
using Domain.Entities;
using MediatR;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Transactions.Commands;

public sealed record DeleteTransactionCommand(Guid Id) : IRequest<Result<bool>>;

public sealed class DeleteTransactionCommandHandler
    : IRequestHandler<DeleteTransactionCommand, Result<bool>>
{
    private readonly IRepository<Transaction> _repository;

    public DeleteTransactionCommandHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<Transaction>();
    }

    public async Task<Result<bool>> Handle(
        DeleteTransactionCommand request,
        CancellationToken cancellationToken)
    {
        var transaction = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (transaction == null || transaction.IsDeleted)
        {
            return Result.Failure<bool>(
                new Error("Transaction.NotFound", "Transaction not found."));
        }

        transaction.IsDeleted = true;
        transaction.DeletedAt = DateTimeExtensions.PostgreSqlUtcNow;
        transaction.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _repository.Update(transaction);

        return Result.Success(true);
    }
}
