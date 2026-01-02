using Application.Abstractions.Data;
using Domain.Entities;
using MediatR;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Transactions.Commands;

public sealed record CreateTransactionCommand(
    Guid UserId,
    Guid? RelationId,
    string? RelationType,
    decimal? Cost,
    string? TransactionType,
    int? TokenUsed,
    string? PaymentMethod,
    string? Status) : IRequest<Result<Transaction>>;

public sealed class CreateTransactionCommandHandler
    : IRequestHandler<CreateTransactionCommand, Result<Transaction>>
{
    private readonly IRepository<Transaction> _repository;

    public CreateTransactionCommandHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<Transaction>();
    }

    public async Task<Result<Transaction>> Handle(
        CreateTransactionCommand request,
        CancellationToken cancellationToken)
    {
        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var transaction = new Transaction
        {
            Id = Guid.CreateVersion7(),
            UserId = request.UserId,
            RelationId = request.RelationId,
            RelationType = NormalizeValue(request.RelationType),
            Cost = request.Cost,
            TransactionType = NormalizeValue(request.TransactionType),
            TokenUsed = request.TokenUsed,
            PaymentMethod = NormalizeValue(request.PaymentMethod),
            Status = NormalizeValue(request.Status),
            CreatedAt = now,
            UpdatedAt = now,
            IsDeleted = false
        };

        await _repository.AddAsync(transaction, cancellationToken);

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
