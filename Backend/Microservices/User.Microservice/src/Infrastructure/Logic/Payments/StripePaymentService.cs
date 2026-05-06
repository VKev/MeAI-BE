using Application.Abstractions.ApiCredentials;
using Application.Abstractions.Payments;
using Infrastructure.Configs;
using Microsoft.Extensions.Options;
using Stripe;

namespace Infrastructure.Logic.Payments;

public sealed class StripePaymentService : IStripePaymentService
{
    private readonly StripeOptions _options;
    private readonly IApiCredentialProvider _credentialProvider;

    public StripePaymentService(
        IOptions<StripeOptions> options,
        IApiCredentialProvider credentialProvider)
    {
        _options = options.Value;
        _credentialProvider = credentialProvider;
    }

    public async Task<StripeCustomerResult> CreateCustomerAsync(
        string? customerEmail,
        string? customerName,
        IDictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
    {
        var customer = await CreateCustomerService().CreateAsync(
            new CustomerCreateOptions
            {
                Email = customerEmail,
                Name = customerName,
                Metadata = metadata.Count == 0 ? null : new Dictionary<string, string>(metadata)
            },
            cancellationToken: cancellationToken);

        return new StripeCustomerResult(customer.Id);
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
            var product = await CreateProductService().CreateAsync(
                new ProductCreateOptions
                {
                    Name = string.IsNullOrWhiteSpace(subscriptionName) ? "Subscription" : subscriptionName.Trim()
                },
                cancellationToken: cancellationToken);

            productId = product.Id;
        }

        var price = await CreatePriceService().CreateAsync(
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
        string? stripeCustomerId,
        string? customerEmail,
        string? customerName,
        IDictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
    {
        var customerId = stripeCustomerId;
        if (string.IsNullOrWhiteSpace(customerId))
        {
            var customer = await CreateCustomerAsync(
                customerEmail,
                customerName,
                metadata,
                cancellationToken);
            customerId = customer.StripeCustomerId;
        }

        var subscription = await CreateSubscriptionService().CreateAsync(
            new SubscriptionCreateOptions
            {
                Customer = customerId,
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

    public async Task<StripeOneTimePaymentResult> CreateCoinPackagePaymentIntentAsync(
        string stripeCustomerId,
        string? customerEmail,
        string? customerName,
        decimal amount,
        string currency,
        string description,
        IDictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
    {
        var currencyCode = ResolveCurrency();

        var paymentIntent = await CreatePaymentIntentService().CreateAsync(
            new PaymentIntentCreateOptions
            {
                Customer = stripeCustomerId,
                Amount = ToMinorAmount(amount),
                Currency = currencyCode,
                Description = description,
                ReceiptEmail = customerEmail,
                Metadata = metadata.Count == 0 ? null : new Dictionary<string, string>(metadata),
                AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
                {
                    Enabled = true
                }
            },
            cancellationToken: cancellationToken);

        return new StripeOneTimePaymentResult(
            paymentIntent.Id,
            paymentIntent.ClientSecret ?? string.Empty,
            NormalizeCheckoutStatus(null, null, paymentIntent.Status),
            paymentIntent.Currency ?? currencyCode,
            ToMajorAmount(paymentIntent.Amount));
    }

    public async Task<StripeRecurringSubscriptionResult> UpgradeSubscriptionAsync(
        string stripeSubscriptionId,
        string stripePriceId,
        IDictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
    {
        var subscription = await GetSubscriptionWithInvoiceAsync(stripeSubscriptionId, cancellationToken);
        var currentItem = GetPrimaryItem(subscription);

        var updatedSubscription = await CreateSubscriptionService().UpdateAsync(
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

        var schedule = await CreateSubscriptionScheduleService().CreateAsync(
            new SubscriptionScheduleCreateOptions
            {
                FromSubscription = stripeSubscriptionId
            },
            cancellationToken: cancellationToken);

        var currentPeriodStart = subscription.CurrentPeriodStart == default
            ? DateTime.UtcNow
            : subscription.CurrentPeriodStart;
        var currentPeriodEnd = subscription.CurrentPeriodEnd;

        await CreateSubscriptionScheduleService().UpdateAsync(
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
            var paymentIntent = await CreatePaymentIntentService().GetAsync(
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

    public async Task<StripeCheckoutStatusResult> GetCoinPackageCheckoutStatusAsync(
        string paymentIntentId,
        CancellationToken cancellationToken = default)
    {
        var paymentIntent = await CreatePaymentIntentService().GetAsync(
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

    public async Task<StripeSubscriptionSnapshotResult> GetSubscriptionSnapshotAsync(
        string stripeSubscriptionId,
        CancellationToken cancellationToken = default)
    {
        var subscription = await GetSubscriptionWithInvoiceAsync(stripeSubscriptionId, cancellationToken);
        var invoice = subscription.LatestInvoice;
        var paymentIntent = invoice?.PaymentIntent;

        return new StripeSubscriptionSnapshotResult(
            subscription.Id,
            subscription.CustomerId,
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
        await CreateSubscriptionService().UpdateAsync(
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
            await CreateSubscriptionScheduleService().ReleaseAsync(
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

        var updatedSubscription = await CreateSubscriptionService().UpdateAsync(
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

    public async Task<IReadOnlyList<StripeCardResult>> GetCustomerCardsAsync(
        string stripeCustomerId,
        string? stripeSubscriptionId,
        CancellationToken cancellationToken = default)
    {
        var defaultPaymentMethodId = await ResolveDefaultPaymentMethodIdAsync(
            stripeCustomerId,
            stripeSubscriptionId,
            cancellationToken);

        var paymentMethods = await CreatePaymentMethodService().ListAsync(
            new PaymentMethodListOptions
            {
                Customer = stripeCustomerId,
                Type = "card"
            },
            cancellationToken: cancellationToken);

        return paymentMethods.Data
            .Select(item => ToCardResult(item, defaultPaymentMethodId))
            .ToList();
    }

    public async Task<StripeCardSetupIntentResult> CreateCardSetupIntentAsync(
        string stripeCustomerId,
        IDictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
    {
        var setupIntent = await CreateSetupIntentService().CreateAsync(
            new SetupIntentCreateOptions
            {
                Customer = stripeCustomerId,
                PaymentMethodTypes = new List<string> { "card" },
                Usage = "off_session",
                Metadata = metadata.Count == 0 ? null : new Dictionary<string, string>(metadata)
            },
            cancellationToken: cancellationToken);

        return new StripeCardSetupIntentResult(
            setupIntent.Id,
            setupIntent.ClientSecret,
            stripeCustomerId);
    }

    public async Task<StripeCardResult> SetDefaultCardAsync(
        string stripeCustomerId,
        string paymentMethodId,
        IEnumerable<string> stripeSubscriptionIds,
        CancellationToken cancellationToken = default)
    {
        var paymentMethodService = CreatePaymentMethodService();
        var subscriptionService = CreateSubscriptionService();
        var customerService = CreateCustomerService();

        var paymentMethod = await paymentMethodService.GetAsync(
            paymentMethodId,
            cancellationToken: cancellationToken);

        if (string.IsNullOrWhiteSpace(paymentMethod.CustomerId))
        {
            paymentMethod = await paymentMethodService.AttachAsync(
                paymentMethodId,
                new PaymentMethodAttachOptions
                {
                    Customer = stripeCustomerId
                },
                cancellationToken: cancellationToken);
        }
        else if (!string.Equals(paymentMethod.CustomerId, stripeCustomerId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Payment method belongs to another Stripe customer.");
        }

        await customerService.UpdateAsync(
            stripeCustomerId,
            new CustomerUpdateOptions
            {
                InvoiceSettings = new CustomerInvoiceSettingsOptions
                {
                    DefaultPaymentMethod = paymentMethodId
                }
            },
            cancellationToken: cancellationToken);

        foreach (var stripeSubscriptionId in stripeSubscriptionIds
                     .Where(item => !string.IsNullOrWhiteSpace(item))
                     .Distinct(StringComparer.Ordinal))
        {
            await subscriptionService.UpdateAsync(
                stripeSubscriptionId,
                new SubscriptionUpdateOptions
                {
                    DefaultPaymentMethod = paymentMethodId
                },
                cancellationToken: cancellationToken);
        }

        return ToCardResult(paymentMethod, paymentMethodId);
    }

    private async Task<Price?> TryGetPriceAsync(string? stripePriceId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stripePriceId))
        {
            return null;
        }

        try
        {
            return await CreatePriceService().GetAsync(stripePriceId, cancellationToken: cancellationToken);
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
        return await CreateSubscriptionService().GetAsync(
            stripeSubscriptionId,
            new SubscriptionGetOptions
            {
                Expand = new List<string> { "latest_invoice.payment_intent" }
            },
            cancellationToken: cancellationToken);
    }

    private async Task<string?> ResolveDefaultPaymentMethodIdAsync(
        string stripeCustomerId,
        string? stripeSubscriptionId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(stripeSubscriptionId))
        {
            var subscription = await CreateSubscriptionService().GetAsync(
                stripeSubscriptionId,
                cancellationToken: cancellationToken);

            if (!string.IsNullOrWhiteSpace(subscription.DefaultPaymentMethodId))
            {
                return subscription.DefaultPaymentMethodId;
            }
        }

        var customer = await CreateCustomerService().GetAsync(
            stripeCustomerId,
            cancellationToken: cancellationToken);

        return customer.InvoiceSettings?.DefaultPaymentMethodId;
    }

    private StripeRecurringSubscriptionResult ToRecurringResult(Stripe.Subscription subscription)
    {
        var invoice = subscription.LatestInvoice;
        var paymentIntent = invoice?.PaymentIntent;
        var currency = invoice?.Currency ?? ResolveCurrency();
        var amountDue = invoice?.AmountDue ?? paymentIntent?.Amount ?? 0L;

        return new StripeRecurringSubscriptionResult(
            subscription.Id,
            subscription.CustomerId,
            NormalizeCheckoutStatus(subscription.Status, invoice?.Status, paymentIntent?.Status),
            paymentIntent?.Id,
            paymentIntent?.ClientSecret,
            currency,
            ToMajorAmount(amountDue),
            subscription.CurrentPeriodStart,
            subscription.CurrentPeriodEnd);
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

    private StripeClient CreateStripeClient()
    {
        var secretKey = _credentialProvider.GetRequiredValue("Stripe", "SecretKey");
        return new StripeClient(secretKey);
    }

    private CustomerService CreateCustomerService() => new(CreateStripeClient());

    private PaymentIntentService CreatePaymentIntentService() => new(CreateStripeClient());

    private PaymentMethodService CreatePaymentMethodService() => new(CreateStripeClient());

    private PriceService CreatePriceService() => new(CreateStripeClient());

    private ProductService CreateProductService() => new(CreateStripeClient());

    private SetupIntentService CreateSetupIntentService() => new(CreateStripeClient());

    private SubscriptionService CreateSubscriptionService() => new(CreateStripeClient());

    private SubscriptionScheduleService CreateSubscriptionScheduleService() => new(CreateStripeClient());

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

    private static StripeCardResult ToCardResult(
        PaymentMethod paymentMethod,
        string? defaultPaymentMethodId)
    {
        var card = paymentMethod.Card;
        var expMonth = card?.ExpMonth;
        var expYear = card?.ExpYear;

        var isExpired = false;
        if (expMonth.HasValue && expYear.HasValue)
        {
            var now = DateTime.UtcNow;
            isExpired = expYear.Value < now.Year ||
                        (expYear.Value == now.Year && expMonth.Value < now.Month);
        }

        return new StripeCardResult(
            paymentMethod.Id,
            card?.Brand,
            card?.Last4,
            expMonth,
            expYear,
            card?.Funding,
            card?.Country,
            paymentMethod.BillingDetails?.Name,
            string.Equals(paymentMethod.Id, defaultPaymentMethodId, StringComparison.Ordinal),
            isExpired);
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
