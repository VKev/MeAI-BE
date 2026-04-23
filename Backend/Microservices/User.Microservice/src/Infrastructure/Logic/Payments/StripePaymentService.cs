using Application.Abstractions.Payments;
using Infrastructure.Configs;
using Microsoft.Extensions.Options;
using Stripe;

namespace Infrastructure.Logic.Payments;

public sealed class StripePaymentService : IStripePaymentService
{
    private readonly StripeOptions _options;
    private readonly CustomerService _customerService;
    private readonly PaymentIntentService _paymentIntentService;
    private readonly PriceService _priceService;
    private readonly ProductService _productService;
    private readonly SubscriptionService _subscriptionService;
    private readonly SubscriptionScheduleService _subscriptionScheduleService;

    public StripePaymentService(IOptions<StripeOptions> options)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            throw new InvalidOperationException("Stripe secret key is not configured.");
        }

        var stripeClient = new StripeClient(_options.SecretKey);
        _customerService = new CustomerService(stripeClient);
        _paymentIntentService = new PaymentIntentService(stripeClient);
        _priceService = new PriceService(stripeClient);
        _productService = new ProductService(stripeClient);
        _subscriptionService = new SubscriptionService(stripeClient);
        _subscriptionScheduleService = new SubscriptionScheduleService(stripeClient);
    }

    public async Task<StripeCatalogPriceResult> EnsureRecurringPriceAsync(
        string? stripeProductId,
        string? stripePriceId,
        decimal amount,
        int durationMonths,
        string? subscriptionName,
        CancellationToken cancellationToken = default)
    {
        var currency = ResolveCurrency();
        var amountMinor = ToMinorAmount(amount);
        var productId = stripeProductId;
        var existingPrice = await TryGetPriceAsync(stripePriceId, cancellationToken);

        if (existingPrice != null && PriceMatches(existingPrice, amountMinor, currency, durationMonths))
        {
            return new StripeCatalogPriceResult(existingPrice.ProductId ?? productId ?? string.Empty, existingPrice.Id);
        }

        if (string.IsNullOrWhiteSpace(productId))
        {
            var product = await _productService.CreateAsync(
                new ProductCreateOptions
                {
                    Name = string.IsNullOrWhiteSpace(subscriptionName) ? "Subscription" : subscriptionName.Trim()
                },
                cancellationToken: cancellationToken);

            productId = product.Id;
        }

        var price = await _priceService.CreateAsync(
            new PriceCreateOptions
            {
                Product = productId,
                Currency = currency,
                UnitAmount = amountMinor,
                Recurring = new PriceRecurringOptions
                {
                    Interval = "month",
                    IntervalCount = durationMonths
                }
            },
            cancellationToken: cancellationToken);

        return new StripeCatalogPriceResult(productId, price.Id);
    }

    public async Task<StripeRecurringSubscriptionResult> CreateSubscriptionAsync(
        string stripePriceId,
        string? customerEmail,
        string? customerName,
        IDictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
    {
        var customer = await _customerService.CreateAsync(
            new CustomerCreateOptions
            {
                Email = customerEmail,
                Name = customerName
            },
            cancellationToken: cancellationToken);

        var subscription = await _subscriptionService.CreateAsync(
            new SubscriptionCreateOptions
            {
                Customer = customer.Id,
                CollectionMethod = "charge_automatically",
                PaymentBehavior = "default_incomplete",
                Items = new List<SubscriptionItemOptions>
                {
                    new()
                    {
                        Price = stripePriceId
                    }
                },
                Metadata = metadata.Count == 0 ? null : new Dictionary<string, string>(metadata),
                PaymentSettings = new SubscriptionPaymentSettingsOptions
                {
                    SaveDefaultPaymentMethod = "on_subscription"
                },
                Expand = new List<string> { "latest_invoice.payment_intent" }
            },
            cancellationToken: cancellationToken);

        return ToRecurringResult(subscription);
    }

    public async Task<StripeRecurringSubscriptionResult> UpgradeSubscriptionAsync(
        string stripeSubscriptionId,
        string stripePriceId,
        IDictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
    {
        var subscription = await GetSubscriptionWithInvoiceAsync(stripeSubscriptionId, cancellationToken);
        var currentItem = GetPrimaryItem(subscription);

        var updatedSubscription = await _subscriptionService.UpdateAsync(
            stripeSubscriptionId,
            new SubscriptionUpdateOptions
            {
                PaymentBehavior = "pending_if_incomplete",
                ProrationBehavior = "always_invoice",
                Items = new List<SubscriptionItemOptions>
                {
                    new()
                    {
                        Id = currentItem.Id,
                        Price = stripePriceId
                    }
                },
                Expand = new List<string> { "latest_invoice.payment_intent" }
            },
            cancellationToken: cancellationToken);

        if (IsSuccessStatus(NormalizeCheckoutStatus(
                updatedSubscription.Status,
                updatedSubscription.LatestInvoice?.Status,
                updatedSubscription.LatestInvoice?.PaymentIntent?.Status)))
        {
            await UpdateSubscriptionMetadataAsync(
                stripeSubscriptionId,
                metadata,
                cancellationToken);

            updatedSubscription = await GetSubscriptionWithInvoiceAsync(stripeSubscriptionId, cancellationToken);
        }

        return ToRecurringResult(updatedSubscription);
    }

    public async Task<StripeScheduledChangeResult> ScheduleSubscriptionChangeAsync(
        string stripeSubscriptionId,
        string currentStripePriceId,
        string nextStripePriceId,
        IDictionary<string, string> currentMetadata,
        IDictionary<string, string> nextPhaseMetadata,
        CancellationToken cancellationToken = default)
    {
        var subscription = await GetSubscriptionWithInvoiceAsync(stripeSubscriptionId, cancellationToken);
        if (subscription.CurrentPeriodEnd == default)
        {
            throw new InvalidOperationException("Current subscription period end is unavailable.");
        }

        if (!string.IsNullOrWhiteSpace(subscription.ScheduleId))
        {
            throw new InvalidOperationException("A Stripe schedule is already attached to this subscription.");
        }

        var schedule = await _subscriptionScheduleService.CreateAsync(
            new SubscriptionScheduleCreateOptions
            {
                FromSubscription = stripeSubscriptionId
            },
            cancellationToken: cancellationToken);

        var currentPeriodStart = subscription.CurrentPeriodStart == default
            ? DateTime.UtcNow
            : subscription.CurrentPeriodStart;
        var currentPeriodEnd = subscription.CurrentPeriodEnd;

        await _subscriptionScheduleService.UpdateAsync(
            schedule.Id,
            new SubscriptionScheduleUpdateOptions
            {
                EndBehavior = "release",
                Phases = new List<SubscriptionSchedulePhaseOptions>
                {
                    new()
                    {
                        StartDate = currentPeriodStart,
                        EndDate = currentPeriodEnd,
                        Items = new List<SubscriptionSchedulePhaseItemOptions>
                        {
                            new()
                            {
                                Price = currentStripePriceId,
                                Quantity = 1
                            }
                        },
                        Metadata = currentMetadata.Count == 0 ? null : new Dictionary<string, string>(currentMetadata),
                        ProrationBehavior = "none"
                    },
                    new()
                    {
                        StartDate = currentPeriodEnd,
                        Items = new List<SubscriptionSchedulePhaseItemOptions>
                        {
                            new()
                            {
                                Price = nextStripePriceId,
                                Quantity = 1
                            }
                        },
                        Iterations = 1,
                        Metadata = nextPhaseMetadata.Count == 0 ? null : new Dictionary<string, string>(nextPhaseMetadata),
                        ProrationBehavior = "none"
                    }
                }
            },
            cancellationToken: cancellationToken);

        return new StripeScheduledChangeResult(
            stripeSubscriptionId,
            schedule.Id,
            "scheduled",
            currentPeriodStart,
            currentPeriodEnd);
    }

    public async Task<StripeCheckoutStatusResult> GetCheckoutStatusAsync(
        string? paymentIntentId,
        string? stripeSubscriptionId,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(stripeSubscriptionId))
        {
            var subscription = await GetSubscriptionWithInvoiceAsync(stripeSubscriptionId, cancellationToken);
            var invoice = subscription.LatestInvoice;
            var paymentIntent = invoice?.PaymentIntent;
            var status = NormalizeCheckoutStatus(subscription.Status, invoice?.Status, paymentIntent?.Status);

            return new StripeCheckoutStatusResult(
                status,
                IsSuccessStatus(status),
                IsTerminalStatus(status),
                invoice?.Id ?? paymentIntent?.Id,
                paymentIntent?.Id,
                subscription.Id);
        }

        if (!string.IsNullOrWhiteSpace(paymentIntentId))
        {
            var paymentIntent = await _paymentIntentService.GetAsync(
                paymentIntentId,
                cancellationToken: cancellationToken);

            var status = NormalizeCheckoutStatus(null, null, paymentIntent.Status);

            return new StripeCheckoutStatusResult(
                status,
                IsSuccessStatus(status),
                IsTerminalStatus(status),
                paymentIntent.Id,
                paymentIntent.Id,
                null);
        }

        throw new InvalidOperationException("Stripe payment identifiers are missing.");
    }

    public async Task<StripeSubscriptionSnapshotResult> GetSubscriptionSnapshotAsync(
        string stripeSubscriptionId,
        CancellationToken cancellationToken = default)
    {
        var subscription = await GetSubscriptionWithInvoiceAsync(stripeSubscriptionId, cancellationToken);
        var invoice = subscription.LatestInvoice;
        var paymentIntent = invoice?.PaymentIntent;

        return new StripeSubscriptionSnapshotResult(
            subscription.Id,
            subscription.Status,
            invoice?.Id,
            paymentIntent?.ClientSecret,
            paymentIntent?.Id,
            subscription.ScheduleId,
            subscription.CurrentPeriodStart,
            subscription.CurrentPeriodEnd);
    }

    public async Task UpdateSubscriptionMetadataAsync(
        string stripeSubscriptionId,
        IDictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
    {
        await _subscriptionService.UpdateAsync(
            stripeSubscriptionId,
            new SubscriptionUpdateOptions
            {
                Metadata = metadata.Count == 0 ? null : new Dictionary<string, string>(metadata)
            },
            cancellationToken: cancellationToken);
    }

    public async Task<StripeAutoRenewUpdateResult> SetSubscriptionAutoRenewAsync(
        string stripeSubscriptionId,
        string? stripeScheduleId,
        bool enabled,
        IDictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
    {
        var subscription = await GetSubscriptionWithInvoiceAsync(stripeSubscriptionId, cancellationToken);
        var attachedScheduleId = string.IsNullOrWhiteSpace(stripeScheduleId)
            ? subscription.ScheduleId
            : stripeScheduleId;

        if (!enabled && !string.IsNullOrWhiteSpace(attachedScheduleId))
        {
            await _subscriptionScheduleService.ReleaseAsync(
                attachedScheduleId,
                new SubscriptionScheduleReleaseOptions(),
                cancellationToken: cancellationToken);

            subscription = await GetSubscriptionWithInvoiceAsync(stripeSubscriptionId, cancellationToken);
        }

        var mergedMetadata = new Dictionary<string, string>(subscription.Metadata ?? new Dictionary<string, string>());
        foreach (var item in metadata)
        {
            mergedMetadata[item.Key] = item.Value;
        }

        var updatedSubscription = await _subscriptionService.UpdateAsync(
            stripeSubscriptionId,
            new SubscriptionUpdateOptions
            {
                CancelAtPeriodEnd = !enabled,
                Metadata = mergedMetadata.Count == 0 ? null : mergedMetadata
            },
            cancellationToken: cancellationToken);

        return new StripeAutoRenewUpdateResult(
            updatedSubscription.Id,
            updatedSubscription.Status,
            updatedSubscription.ScheduleId,
            updatedSubscription.CurrentPeriodEnd);
    }

    private async Task<Price?> TryGetPriceAsync(string? stripePriceId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stripePriceId))
        {
            return null;
        }

        try
        {
            return await _priceService.GetAsync(stripePriceId, cancellationToken: cancellationToken);
        }
        catch (StripeException)
        {
            return null;
        }
    }

    private async Task<Stripe.Subscription> GetSubscriptionWithInvoiceAsync(
        string stripeSubscriptionId,
        CancellationToken cancellationToken)
    {
        return await _subscriptionService.GetAsync(
            stripeSubscriptionId,
            new SubscriptionGetOptions
            {
                Expand = new List<string> { "latest_invoice.payment_intent" }
            },
            cancellationToken: cancellationToken);
    }

    private static SubscriptionItem GetPrimaryItem(Stripe.Subscription subscription)
    {
        var item = subscription.Items?.Data?.FirstOrDefault();
        if (item == null)
        {
            throw new InvalidOperationException("Stripe subscription has no subscription items.");
        }

        return item;
    }

    private StripeRecurringSubscriptionResult ToRecurringResult(Stripe.Subscription subscription)
    {
        var invoice = subscription.LatestInvoice;
        var paymentIntent = invoice?.PaymentIntent;
        var currency = invoice?.Currency ?? ResolveCurrency();
        var amountDue = invoice?.AmountDue ?? paymentIntent?.Amount ?? 0L;

        return new StripeRecurringSubscriptionResult(
            subscription.Id,
            NormalizeCheckoutStatus(subscription.Status, invoice?.Status, paymentIntent?.Status),
            paymentIntent?.Id,
            paymentIntent?.ClientSecret,
            currency,
            ToMajorAmount(amountDue),
            subscription.CurrentPeriodStart,
            subscription.CurrentPeriodEnd);
    }

    private bool PriceMatches(Price price, long amountMinor, string currency, int durationMonths)
    {
        if (price.Active != true)
        {
            return false;
        }

        if (!string.Equals(price.Currency, currency, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (price.UnitAmount != amountMinor)
        {
            return false;
        }

        var recurring = price.Recurring;
        return recurring != null &&
               string.Equals(recurring.Interval, "month", StringComparison.OrdinalIgnoreCase) &&
               recurring.IntervalCount == durationMonths;
    }

    private string ResolveCurrency()
    {
        return string.IsNullOrWhiteSpace(_options.Currency)
            ? "vnd"
            : _options.Currency.Trim().ToLowerInvariant();
    }

    private int ResolveCurrencyDecimals()
    {
        return _options.CurrencyDecimals <= 0 ? 0 : _options.CurrencyDecimals;
    }

    private long ToMinorAmount(decimal amount)
    {
        var decimals = ResolveCurrencyDecimals();
        return Convert.ToInt64(
            Math.Round(amount * Convert.ToDecimal(Math.Pow(10, decimals)), MidpointRounding.AwayFromZero));
    }

    private decimal ToMajorAmount(long amountMinor)
    {
        var decimals = ResolveCurrencyDecimals();
        if (decimals == 0)
        {
            return amountMinor;
        }

        return amountMinor / Convert.ToDecimal(Math.Pow(10, decimals));
    }

    private static string NormalizeCheckoutStatus(
        string? subscriptionStatus,
        string? invoiceStatus,
        string? paymentIntentStatus)
    {
        if (MatchesStatus(subscriptionStatus, "active", "trialing") ||
            MatchesStatus(invoiceStatus, "paid") ||
            MatchesStatus(paymentIntentStatus, "succeeded"))
        {
            return "succeeded";
        }

        if (MatchesStatus(subscriptionStatus, "past_due"))
        {
            return "pending";
        }

        if (MatchesStatus(subscriptionStatus, "canceled", "cancelled", "unpaid", "incomplete_expired") ||
            MatchesStatus(invoiceStatus, "void", "uncollectible") ||
            MatchesStatus(paymentIntentStatus, "canceled", "cancelled"))
        {
            return "failed";
        }

        if (MatchesStatus(paymentIntentStatus, "processing", "requires_action", "requires_payment_method"))
        {
            return "pending";
        }

        return "incomplete";
    }

    private static bool MatchesStatus(string? value, params string[] candidates)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return candidates.Any(candidate => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSuccessStatus(string status) =>
        string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase);

    private static bool IsTerminalStatus(string status) =>
        string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase);
}
