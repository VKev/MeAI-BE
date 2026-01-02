using Application.Abstractions.Data;
using Application.Abstractions.Payments;
using Application.Subscriptions.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Subscriptions.Commands;

public sealed record PurchaseSubscriptionCommand(
    Guid SubscriptionId,
    Guid UserId,
    string? PaymentMethodId) : IRequest<Result<PurchaseSubscriptionResponse>>;

public sealed class PurchaseSubscriptionCommandHandler
    : IRequestHandler<PurchaseSubscriptionCommand, Result<PurchaseSubscriptionResponse>>
{
    private readonly IRepository<Subscription> _subscriptionRepository;
    private readonly IRepository<Transaction> _transactionRepository;
    private readonly IRepository<UserSubscription> _userSubscriptionRepository;
    private readonly IStripePaymentService _stripePaymentService;

    public PurchaseSubscriptionCommandHandler(
        IUnitOfWork unitOfWork,
        IStripePaymentService stripePaymentService)
    {
        _subscriptionRepository = unitOfWork.Repository<Subscription>();
        _transactionRepository = unitOfWork.Repository<Transaction>();
        _userSubscriptionRepository = unitOfWork.Repository<UserSubscription>();
        _stripePaymentService = stripePaymentService;
    }

    public async Task<Result<PurchaseSubscriptionResponse>> Handle(
        PurchaseSubscriptionCommand request,
        CancellationToken cancellationToken)
    {
        var subscription = await _subscriptionRepository.GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Id == request.SubscriptionId && !item.IsDeleted,
                cancellationToken);

        if (subscription == null)
        {
            return Result.Failure<PurchaseSubscriptionResponse>(
                new Error("Subscription.NotFound", "Subscription not found."));
        }

        if (!subscription.Cost.HasValue || subscription.Cost.Value <= 0)
        {
            return Result.Failure<PurchaseSubscriptionResponse>(
                new Error("Subscription.InvalidCost", "Subscription cost is not valid."));
        }

        var cost = Convert.ToDecimal(subscription.Cost.Value);
        StripePaymentIntentResult paymentIntent;
        var paymentMethodId = string.IsNullOrWhiteSpace(request.PaymentMethodId)
            ? null
            : request.PaymentMethodId.Trim();
        try
        {
            paymentIntent = await _stripePaymentService.CreatePaymentIntentAsync(
                cost,
                paymentMethodId,
                new Dictionary<string, string>
                {
                    ["subscription_id"] = subscription.Id.ToString(),
                    ["user_id"] = request.UserId.ToString()
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            return Result.Failure<PurchaseSubscriptionResponse>(
                new Error("Stripe.PaymentFailed", ex.Message));
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var transaction = new Transaction
        {
            Id = Guid.CreateVersion7(),
            UserId = request.UserId,
            RelationId = subscription.Id,
            RelationType = "Subscription",
            Cost = cost,
            TransactionType = "SubscriptionPurchase",
            PaymentMethod = "Stripe",
            Status = paymentIntent.Status,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _transactionRepository.AddAsync(transaction, cancellationToken);

        Guid? userSubscriptionId = null;
        var subscriptionActivated = false;
        if (string.Equals(paymentIntent.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
        {
            var userSubscription = new UserSubscription
            {
                Id = Guid.CreateVersion7(),
                UserId = request.UserId,
                SubscriptionId = subscription.Id,
                ActiveDate = now,
                Status = "Active",
                CreatedAt = now,
                UpdatedAt = now
            };

            await _userSubscriptionRepository.AddAsync(userSubscription, cancellationToken);
            userSubscriptionId = userSubscription.Id;
            subscriptionActivated = true;
        }

        var response = new PurchaseSubscriptionResponse(
            subscription.Id,
            subscription.Cost.Value,
            paymentIntent.Currency,
            paymentIntent.Amount,
            paymentIntent.PaymentIntentId,
            paymentIntent.ClientSecret,
            paymentIntent.Status,
            transaction.Id,
            subscriptionActivated,
            userSubscriptionId);

        return Result.Success(response);
    }
}
