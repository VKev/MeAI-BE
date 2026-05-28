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

public sealed record ToggleSubscriptionStatusCommand(
    Guid Id,
    bool IsActive) : IRequest<Result<Subscription>>;

public sealed class ToggleSubscriptionStatusCommandHandler
    : IRequestHandler<ToggleSubscriptionStatusCommand, Result<Subscription>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRepository<Subscription> _subscriptionRepository;
    private readonly IRepository<UserSubscription> _userSubscriptionRepository;
    private readonly IStripePaymentService _stripePaymentService;
    private readonly ILogger<ToggleSubscriptionStatusCommandHandler> _logger;

    public ToggleSubscriptionStatusCommandHandler(
        IUnitOfWork unitOfWork,
        IStripePaymentService stripePaymentService,
        ILogger<ToggleSubscriptionStatusCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _subscriptionRepository = unitOfWork.Repository<Subscription>();
        _userSubscriptionRepository = unitOfWork.Repository<UserSubscription>();
        _stripePaymentService = stripePaymentService;
        _logger = logger;
    }

    public async Task<Result<Subscription>> Handle(
        ToggleSubscriptionStatusCommand request,
        CancellationToken cancellationToken)
    {
        var subscription = await _subscriptionRepository.GetByIdAsync(request.Id, cancellationToken);
        if (subscription is null)
        {
            return Result.Failure<Subscription>(
                new Error("Subscription.NotFound", "Subscription not found."));
        }

        subscription.IsActive = request.IsActive;
        subscription.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _subscriptionRepository.Update(subscription);

        if (!string.IsNullOrWhiteSpace(subscription.StripeProductId) ||
            !string.IsNullOrWhiteSpace(subscription.StripePriceId))
        {
            try
            {
                await _stripePaymentService.SetCatalogActiveAsync(
                    subscription.StripeProductId,
                    subscription.StripePriceId,
                    request.IsActive,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to set Stripe catalog active={Active} for subscription {SubscriptionId}.",
                    request.IsActive,
                    request.Id);
            }
        }

        // When deactivating a plan, disable recurring for all active subscribers
        // so they only keep the plan until their current period ends
        if (!request.IsActive)
        {
            var activeUserSubscriptions = await _userSubscriptionRepository.GetAll()
                .Where(us =>
                    us.SubscriptionId == request.Id &&
                    !us.IsDeleted &&
                    (us.Status == null || us.Status.ToLower() == "active"))
                .ToListAsync(cancellationToken);

            foreach (var userSub in activeUserSubscriptions)
            {
                // Mark as non-renewable — user keeps access until EndDate
                userSub.Status = "non_renewable";
                userSub.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
                _userSubscriptionRepository.Update(userSub);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(subscription);
    }
}
