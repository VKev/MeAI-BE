using Application.Abstractions.Data;
using Application.Transactions.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Transactions.Queries;

public sealed record GetUserTransactionsQuery(Guid UserId, bool IncludeDeleted = false)
    : IRequest<Result<List<TransactionResponse>>>;

public sealed class GetUserTransactionsQueryHandler
    : IRequestHandler<GetUserTransactionsQuery, Result<List<TransactionResponse>>>
{
    private readonly IRepository<Transaction> _transactionRepository;
    private readonly IRepository<Subscription> _subscriptionRepository;

    public GetUserTransactionsQueryHandler(IUnitOfWork unitOfWork)
    {
        _transactionRepository = unitOfWork.Repository<Transaction>();
        _subscriptionRepository = unitOfWork.Repository<Subscription>();
    }

    public async Task<Result<List<TransactionResponse>>> Handle(
        GetUserTransactionsQuery request,
        CancellationToken cancellationToken)
    {
        var query = _transactionRepository.GetAll()
            .AsNoTracking()
            .Where(item => item.UserId == request.UserId);

        if (!request.IncludeDeleted)
        {
            query = query.Where(item => !item.IsDeleted);
        }

        var transactions = await query
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken);

        var subscriptions = await BuildSubscriptionLookupAsync(transactions, cancellationToken);
        var response = transactions
            .Select(item => ToResponse(item, subscriptions))
            .ToList();

        return Result.Success(response);
    }

    private async Task<Dictionary<Guid, TransactionSubscriptionRelationResponse>> BuildSubscriptionLookupAsync(
        IEnumerable<Transaction> transactions,
        CancellationToken cancellationToken)
    {
        var subscriptionIds = transactions
            .Where(item =>
                item.RelationId.HasValue &&
                string.Equals(item.RelationType, RelationTypeSubscription, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.RelationId!.Value)
            .Distinct()
            .ToList();

        if (subscriptionIds.Count == 0)
        {
            return new Dictionary<Guid, TransactionSubscriptionRelationResponse>();
        }

        var subscriptions = await _subscriptionRepository.GetAll()
            .AsNoTracking()
            .Where(item => subscriptionIds.Contains(item.Id) && !item.IsDeleted)
            .Select(item => new TransactionSubscriptionRelationResponse(
                item.Id,
                item.Name,
                item.Cost,
                item.DurationMonths,
                item.MeAiCoin))
            .ToListAsync(cancellationToken);

        return subscriptions.ToDictionary(item => item.Id);
    }

    private static TransactionResponse ToResponse(
        Transaction transaction,
        IReadOnlyDictionary<Guid, TransactionSubscriptionRelationResponse> subscriptions)
    {
        var relationType = Normalize(transaction.RelationType);
        TransactionRelationInfo? relation = null;

        if (transaction.RelationId.HasValue && relationType != null)
        {
            TransactionSubscriptionRelationResponse? subscription = null;

            if (string.Equals(relationType, RelationTypeSubscription, StringComparison.OrdinalIgnoreCase) &&
                subscriptions.TryGetValue(transaction.RelationId.Value, out var relatedSubscription))
            {
                subscription = relatedSubscription;
            }

            relation = new TransactionRelationInfo(
                relationType,
                transaction.RelationId.Value,
                subscription);
        }

        return new TransactionResponse(
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
            null);
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
