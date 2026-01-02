using Application.Abstractions.Data;
using Domain.Entities;
using MediatR;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Transactions.Commands;

public sealed record UpdateTransactionCommand(
    Guid Id,
    Guid UserId,
    Guid? RelationId,
    string? RelationType,
    decimal? Cost,
    string? TransactionType,
    int? TokenUsed,
    string? PaymentMethod,
    string? Status) : IRequest<Result<Transaction>>;

public sealed class UpdateTransactionCommandHandler
    : IRequestHandler<UpdateTransactionCommand, Result<Transaction>>
{
    private readonly IRepository<Transaction> _repository;

    public UpdateTransactionCommandHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<Transaction>();
    }

    public async Task<Result<Transaction>> Handle(
        UpdateTransactionCommand request,
        CancellationToken cancellationToken)
    {
        var transaction = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (transaction == null || transaction.IsDeleted)
        {
            return Result.Failure<Transaction>(
                new Error("Transaction.NotFound", "Transaction not found."));
        }

        transaction.UserId = request.UserId;
        transaction.RelationId = request.RelationId;
        transaction.RelationType = NormalizeValue(request.RelationType);
        transaction.Cost = request.Cost;
        transaction.TransactionType = NormalizeValue(request.TransactionType);
        transaction.TokenUsed = request.TokenUsed;
        transaction.PaymentMethod = NormalizeValue(request.PaymentMethod);
        transaction.Status = NormalizeValue(request.Status);
        transaction.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;

        return Result.Success(transaction);
    }

    private static string? NormalizeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
