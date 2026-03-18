using Application.Abstractions.Data;
using Application.Transactions.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Transactions.Queries;

public sealed record GetTransactionByIdQuery(Guid Id, bool IncludeDeleted) : IRequest<Result<TransactionResponse>>;

public sealed class GetTransactionByIdQueryHandler
    : IRequestHandler<GetTransactionByIdQuery, Result<TransactionResponse>>
{
    private readonly IRepository<Transaction> _transactionRepository;
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<Subscription> _subscriptionRepository;

    public GetTransactionByIdQueryHandler(IUnitOfWork unitOfWork)
    {
        _transactionRepository = unitOfWork.Repository<Transaction>();
        _userRepository = unitOfWork.Repository<User>();
        _subscriptionRepository = unitOfWork.Repository<Subscription>();
    }

    public async Task<Result<TransactionResponse>> Handle(
        GetTransactionByIdQuery request,
        CancellationToken cancellationToken)
    {
        var query = _transactionRepository.GetAll()
            .AsNoTracking()
            .Where(item => item.Id == request.Id);

        if (!request.IncludeDeleted)
        {
            query = query.Where(item => !item.IsDeleted);
        }

        var transaction = await query.FirstOrDefaultAsync(cancellationToken);
        if (transaction == null)
        {
            return Result.Failure<TransactionResponse>(
                new Error("Transaction.NotFound", "Transaction not found."));
        }

        var user = await _userRepository.GetAll()
            .AsNoTracking()
            .Where(item => item.Id == transaction.UserId)
            .Select(item => new TransactionUserSummaryResponse(
                item.Id,
                item.Username,
                item.Email,
                item.FullName,
                item.IsDeleted))
            .FirstOrDefaultAsync(cancellationToken);

        TransactionRelationInfo? relation = null;
        var relationType = Normalize(transaction.RelationType);
        if (transaction.RelationId.HasValue && relationType != null)
        {
            TransactionSubscriptionRelationResponse? subscription = null;
            if (string.Equals(relationType, RelationTypeSubscription, StringComparison.OrdinalIgnoreCase))
            {
                subscription = await _subscriptionRepository.GetAll()
                    .AsNoTracking()
                    .Where(item => item.Id == transaction.RelationId.Value && !item.IsDeleted)
                    .Select(item => new TransactionSubscriptionRelationResponse(
                        item.Id,
                        item.Name,
                        item.Cost,
                        item.DurationMonths,
                        item.MeAiCoin))
                    .FirstOrDefaultAsync(cancellationToken);
            }

            relation = new TransactionRelationInfo(
                relationType,
                transaction.RelationId.Value,
                subscription);
        }

        return Result.Success(new TransactionResponse(
            transaction.Id,
            transaction.UserId,
            transaction.RelationId,
            transaction.RelationType,
            transaction.Cost,
            transaction.TransactionType,
            transaction.TokenUsed,
            transaction.PaymentMethod,
            transaction.Status,
            transaction.CreatedAt,
            transaction.UpdatedAt,
            transaction.DeletedAt,
            transaction.IsDeleted,
            relation,
            user));
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private const string RelationTypeSubscription = "Subscription";
}
