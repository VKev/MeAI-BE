using Application.Abstractions.Data;
using Application.Abstractions.Payments;
using Application.Subscriptions.Helpers;
using Domain.Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Subscriptions.Commands;

public sealed record CreateSubscriptionCommand(
    string? Name,
    float? Cost,
    int DurationMonths,
    decimal? MeAiCoin,
    string? StripeProductId,
    string? StripePriceId,
    SubscriptionLimits? Limits) : IRequest<Result<Subscription>>;

public sealed class CreateSubscriptionCommandHandler
    : IRequestHandler<CreateSubscriptionCommand, Result<Subscription>>
{
    private readonly IRepository<Subscription> _repository;
    private readonly IStripePaymentService _stripePaymentService;
    private readonly ILogger<CreateSubscriptionCommandHandler> _logger;

    public CreateSubscriptionCommandHandler(
        IUnitOfWork unitOfWork,
        IStripePaymentService stripePaymentService,
        ILogger<CreateSubscriptionCommandHandler> logger)
    {
        _repository = unitOfWork.Repository<Subscription>();
        _stripePaymentService = stripePaymentService;
        _logger = logger;
    }

    public async Task<Result<Subscription>> Handle(
        CreateSubscriptionCommand request,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var name = SubscriptionHelpers.NormalizeName(request.Name);
        var cost = request.Cost ?? 0;
        var requestedStripeProductId = NormalizeStripeId(request.StripeProductId);
        var requestedStripePriceId = NormalizeStripeId(request.StripePriceId);
        var manuallyProvidedStripeIds =
            requestedStripeProductId != null ||
            requestedStripePriceId != null;

        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            Name = name,
            Cost = request.Cost,
            DurationMonths = request.DurationMonths,
            MeAiCoin = request.MeAiCoin,
            Limits = request.Limits,
            CreatedAt = now,
            UpdatedAt = now
        };

        // Auto-create Stripe product and price
        if (cost > 0)
        {
            try
            {
                var stripeResult = await _stripePaymentService.EnsureRecurringPriceAsync(
                    requestedStripeProductId,
                    requestedStripePriceId,
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
                    "Failed to create Stripe product/price for subscription '{Name}'. Continuing without Stripe integration.",
                    name);

                if (manuallyProvidedStripeIds)
                {
                    return Result.Failure<Subscription>(
                        new Error("Stripe.CatalogSyncFailed", "Failed to sync the provided Stripe product or price ID."));
                }
            }
        }
        else
        {
            subscription.StripeProductId = requestedStripeProductId;
            subscription.StripePriceId = requestedStripePriceId;
        }

        await _repository.AddAsync(subscription, cancellationToken);

        return Result.Success(subscription);
    }

    private static string? NormalizeStripeId(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
