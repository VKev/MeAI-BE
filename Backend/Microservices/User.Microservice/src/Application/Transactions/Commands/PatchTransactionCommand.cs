using Application.Abstractions.Data;
using Domain.Entities;
using MediatR;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Transactions.Commands;

public sealed record PatchTransactionCommand(
    Guid Id,
    Guid? UserId,
    Guid? RelationId,
    string? RelationType,
    decimal? Cost,
    string? TransactionType,
    int? TokenUsed,
    string? PaymentMethod,
    string? Status) : IRequest<Result<Transaction>>;

public sealed class PatchTransactionCommandHandler
    : IRequestHandler<PatchTransactionCommand, Result<Transaction>>
{
    private readonly IRepository<Transaction> _repository;

    public PatchTransactionCommandHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<Transaction>();
    }

    public async Task<Result<Transaction>> Handle(
        PatchTransactionCommand request,
        CancellationToken cancellationToken)
    {
        var transaction = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (transaction == null || transaction.IsDeleted)
        {
            return Result.Failure<Transaction>(
                new Error("Transaction.NotFound", "Transaction not found."));
        }

        var updated = false;

        if (request.UserId.HasValue)
        {
            transaction.UserId = request.UserId.Value;
            updated = true;
        }

        if (request.RelationId.HasValue)
        {
            transaction.RelationId = request.RelationId.Value;
            updated = true;
        }

        if (request.RelationType != null)
        {
            transaction.RelationType = NormalizeValue(request.RelationType);
            updated = true;
        }

        if (request.Cost.HasValue)
        {
            transaction.Cost = request.Cost;
            updated = true;
        }

        if (request.TransactionType != null)
        {
            transaction.TransactionType = NormalizeValue(request.TransactionType);
            updated = true;
        }

        if (request.TokenUsed.HasValue)
        {
            transaction.TokenUsed = request.TokenUsed;
            updated = true;
        }

        if (request.PaymentMethod != null)
        {
            transaction.PaymentMethod = NormalizeValue(request.PaymentMethod);
            updated = true;
        }

        if (request.Status != null)
        {
            transaction.Status = NormalizeValue(request.Status);
            updated = true;
        }

        if (updated)
        {
            transaction.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        }

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
