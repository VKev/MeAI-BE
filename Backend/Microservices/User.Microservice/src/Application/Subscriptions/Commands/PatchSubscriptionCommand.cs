using Application.Abstractions.Data;
using Application.Abstractions.Payments;
using Application.Subscriptions.Helpers;
using Domain.Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Subscriptions.Commands;

public sealed record PatchSubscriptionCommand(
    Guid Id,
    string? Name,
    float? Cost,
    int? DurationMonths,
    decimal? MeAiCoin,
    string? StripeProductId,
    string? StripePriceId,
    SubscriptionLimits? Limits) : IRequest<Result<Subscription>>;

public sealed class PatchSubscriptionCommandHandler
    : IRequestHandler<PatchSubscriptionCommand, Result<Subscription>>
{
    private readonly IRepository<Subscription> _repository;
    private readonly IStripePaymentService _stripePaymentService;
    private readonly ILogger<PatchSubscriptionCommandHandler> _logger;

    public PatchSubscriptionCommandHandler(
        IUnitOfWork unitOfWork,
        IStripePaymentService stripePaymentService,
        ILogger<PatchSubscriptionCommandHandler> logger)
    {
        _repository = unitOfWork.Repository<Subscription>();
        _stripePaymentService = stripePaymentService;
        _logger = logger;
    }

    public async Task<Result<Subscription>> Handle(
        PatchSubscriptionCommand request,
        CancellationToken cancellationToken)
    {
        Subscription? subscription = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (subscription == null)
        {
            return Result.Failure<Subscription>(
                new Error("Subscription.NotFound", "Subscription not found."));
        }

        var updated = false;

        var oldCost = subscription.Cost;
        var oldDurationMonths = subscription.DurationMonths;
        var oldStripeProductId = subscription.StripeProductId;
        var oldStripePriceId = subscription.StripePriceId;

        if (request.Name != null)
        {
            subscription.Name = SubscriptionHelpers.NormalizeName(request.Name);
            updated = true;
        }

        if (request.Cost.HasValue)
        {
            subscription.Cost = request.Cost;
            updated = true;
        }

        if (request.DurationMonths.HasValue)
        {
            subscription.DurationMonths = request.DurationMonths.Value;
            updated = true;
        }

        if (request.MeAiCoin.HasValue)
        {
            subscription.MeAiCoin = request.MeAiCoin;
            updated = true;
        }

        if (request.StripeProductId != null)
        {
            subscription.StripeProductId = NormalizeStripeId(request.StripeProductId);
            updated = true;
        }

        if (request.StripePriceId != null)
        {
            subscription.StripePriceId = NormalizeStripeId(request.StripePriceId);
            updated = true;
        }

        if (request.Limits != null)
        {
            subscription.Limits ??= new SubscriptionLimits();
            updated |= SubscriptionHelpers.ApplyLimitsPatch(subscription.Limits, request.Limits);
        }

        var cost = subscription.Cost ?? 0;
        var costChanged = request.Cost.HasValue && oldCost != subscription.Cost;
        var durationChanged = request.DurationMonths.HasValue && oldDurationMonths != subscription.DurationMonths;
        var productChanged = request.StripeProductId != null &&
            !string.Equals(oldStripeProductId, subscription.StripeProductId, StringComparison.Ordinal);
        var priceChanged = request.StripePriceId != null &&
            !string.Equals(oldStripePriceId, subscription.StripePriceId, StringComparison.Ordinal);

        if (cost > 0 && (costChanged || durationChanged || productChanged || priceChanged || string.IsNullOrWhiteSpace(subscription.StripePriceId)))
        {
            try
            {
                var stripeResult = await _stripePaymentService.EnsureRecurringPriceAsync(
                    subscription.StripeProductId,
                    subscription.StripePriceId,
                    (decimal)cost,
                    subscription.DurationMonths,
                    subscription.Name,
                    cancellationToken);

                subscription.StripeProductId = stripeResult.StripeProductId;
                subscription.StripePriceId = stripeResult.StripePriceId;
                updated = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to patch Stripe product/price for subscription '{Name}'. Continuing without Stripe update.",
                    subscription.Name);
            }
        }

        if (updated)
        {
            subscription.UpdatedAt = DateTime.UtcNow;
        }

        return Result.Success(subscription);
    }

    private static string? NormalizeStripeId(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
