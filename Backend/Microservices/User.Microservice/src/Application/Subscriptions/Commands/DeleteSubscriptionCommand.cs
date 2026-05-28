using Application.Abstractions.Data;
using Application.Abstractions.Payments;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Subscriptions.Commands;

public sealed record DeleteSubscriptionCommand(Guid Id) : IRequest<Result<bool>>;

public sealed class DeleteSubscriptionCommandHandler : IRequestHandler<DeleteSubscriptionCommand, Result<bool>>
{
    private readonly IRepository<Subscription> _subscriptionRepository;
    private readonly IRepository<UserSubscription> _userSubscriptionRepository;
    private readonly IStripePaymentService _stripePaymentService;
    private readonly ILogger<DeleteSubscriptionCommandHandler> _logger;

    public DeleteSubscriptionCommandHandler(
        IUnitOfWork unitOfWork,
        IStripePaymentService stripePaymentService,
        ILogger<DeleteSubscriptionCommandHandler> logger)
    {
        _subscriptionRepository = unitOfWork.Repository<Subscription>();
        _userSubscriptionRepository = unitOfWork.Repository<UserSubscription>();
        _stripePaymentService = stripePaymentService;
        _logger = logger;
    }

    public async Task<Result<bool>> Handle(DeleteSubscriptionCommand request, CancellationToken cancellationToken)
    {
        var subscription = await _subscriptionRepository.GetByIdAsync(request.Id, cancellationToken);
        if (subscription == null || subscription.IsDeleted)
        {
            return Result.Failure<bool>(
                new Error("Subscription.NotFound", "Subscription not found."));
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        subscription.IsActive = false;
        subscription.IsDeleted = true;
        subscription.DeletedAt = now;
        subscription.UpdatedAt = now;
        _subscriptionRepository.Update(subscription);

        if (!string.IsNullOrWhiteSpace(subscription.StripeProductId) ||
            !string.IsNullOrWhiteSpace(subscription.StripePriceId))
        {
            try
            {
                await _stripePaymentService.SetCatalogActiveAsync(
                    subscription.StripeProductId,
                    subscription.StripePriceId,
                    false,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to archive Stripe catalog for deleted subscription {SubscriptionId}.",
                    request.Id);
            }
        }

        var currentUserSubscriptions = await _userSubscriptionRepository.GetAll()
            .Where(item =>
                item.SubscriptionId == request.Id &&
                !item.IsDeleted &&
                (item.Status == null || item.Status.ToLower() == "active"))
            .ToListAsync(cancellationToken);

        foreach (var userSubscription in currentUserSubscriptions)
        {
            userSubscription.Status = "non_renewable";
            userSubscription.UpdatedAt = now;
            _userSubscriptionRepository.Update(userSubscription);
        }

        return Result.Success(true);
    }
}
