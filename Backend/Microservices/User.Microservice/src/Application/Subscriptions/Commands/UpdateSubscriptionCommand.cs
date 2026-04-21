using Application.Abstractions.Data;
using Application.Abstractions.Payments;
using Application.Subscriptions.Helpers;
using Domain.Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Subscriptions.Commands;

public sealed record UpdateSubscriptionCommand(
    Guid Id,
    string? Name,
    float? Cost,
    int DurationMonths,
    decimal? MeAiCoin,
    SubscriptionLimits? Limits) : IRequest<Result<Subscription>>;

public sealed class UpdateSubscriptionCommandHandler
    : IRequestHandler<UpdateSubscriptionCommand, Result<Subscription>>
{
    private readonly IRepository<Subscription> _repository;
    private readonly IStripePaymentService _stripePaymentService;
    private readonly ILogger<UpdateSubscriptionCommandHandler> _logger;

    public UpdateSubscriptionCommandHandler(
        IUnitOfWork unitOfWork,
        IStripePaymentService stripePaymentService,
        ILogger<UpdateSubscriptionCommandHandler> logger)
    {
        _repository = unitOfWork.Repository<Subscription>();
        _stripePaymentService = stripePaymentService;
        _logger = logger;
    }

    public async Task<Result<Subscription>> Handle(
        UpdateSubscriptionCommand request,
        CancellationToken cancellationToken)
    {
        Subscription? subscription = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (subscription == null)
        {
            return Result.Failure<Subscription>(
                new Error("Subscription.NotFound", "Subscription not found."));
        }

        var name = SubscriptionHelpers.NormalizeName(request.Name);
        var cost = request.Cost ?? 0;
        var costChanged = subscription.Cost != request.Cost;
        var durationChanged = subscription.DurationMonths != request.DurationMonths;

        subscription.Name = name;
        subscription.Cost = request.Cost;
        subscription.DurationMonths = request.DurationMonths;
        subscription.MeAiCoin = request.MeAiCoin;
        subscription.Limits = request.Limits;
        subscription.UpdatedAt = DateTime.UtcNow;

        // Re-create Stripe price if cost or duration changed
        if (cost > 0 && (costChanged || durationChanged || string.IsNullOrEmpty(subscription.StripePriceId)))
        {
            try
            {
                var stripeResult = await _stripePaymentService.EnsureRecurringPriceAsync(
                    subscription.StripeProductId,
                    null,
                    (decimal)cost,
                    request.DurationMonths,
                    name,
                    cancellationToken);

                subscription.StripeProductId = stripeResult.StripeProductId;
                subscription.StripePriceId = stripeResult.StripePriceId;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to update Stripe product/price for subscription '{Name}'. Continuing without Stripe update.",
                    name);
            }
        }

        return Result.Success(subscription);
    }
}
