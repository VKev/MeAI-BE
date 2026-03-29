using Application.Abstractions.Payments;
using Infrastructure.Configs;
using Microsoft.Extensions.Options;
using Stripe;

namespace Infrastructure.Logic.Payments;

public sealed class StripePaymentService : IStripePaymentService
{
    private readonly StripeOptions _options;
    private readonly PaymentIntentService _paymentIntentService;
    private readonly CustomerService _customerService;
    private readonly SubscriptionService _subscriptionService;
    private readonly ProductService _productService;

    public StripePaymentService(IOptions<StripeOptions> options)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            throw new InvalidOperationException("Stripe secret key is not configured.");
        }

        var stripeClient = new StripeClient(_options.SecretKey);
        _paymentIntentService = new PaymentIntentService(stripeClient);
        _customerService = new CustomerService(stripeClient);
        _subscriptionService = new SubscriptionService(stripeClient);
        _productService = new ProductService(stripeClient);
    }

    public async Task<StripePaymentIntentResult> CreatePaymentIntentAsync(
        decimal amount,
        string? paymentMethodId,
        IDictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
    {
        var currency = string.IsNullOrWhiteSpace(_options.Currency)
            ? "usd"
            : _options.Currency.Trim().ToLowerInvariant();
        var decimals = _options.CurrencyDecimals <= 0 ? 2 : _options.CurrencyDecimals;
        var amountMinor = Convert.ToInt64(
            Math.Round(amount * Convert.ToDecimal(Math.Pow(10, decimals)), MidpointRounding.AwayFromZero));

        if (amountMinor <= 0)
        {
            throw new InvalidOperationException("Payment amount must be greater than zero.");
        }

        var createOptions = new PaymentIntentCreateOptions
        {
            Amount = amountMinor,
            Currency = currency,
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
            {
                Enabled = true
            },
            Metadata = metadata.Count == 0 ? null : new Dictionary<string, string>(metadata)
        };

        if (!string.IsNullOrWhiteSpace(paymentMethodId))
        {
            createOptions.PaymentMethod = paymentMethodId;
            createOptions.Confirm = true;
        }

        var intent = await _paymentIntentService.CreateAsync(createOptions, cancellationToken: cancellationToken);

        return new StripePaymentIntentResult(
            intent.Id,
            intent.ClientSecret,
            intent.Status,
            intent.Currency ?? currency,
            amountMinor);
    }

    public async Task<StripeSubscriptionResult> CreateSubscriptionAsync(
        decimal amount,
        int durationMonths,
        string? paymentMethodId,
        string? customerEmail,
        string? customerName,
        string? subscriptionName,
        IDictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
    {
        var currency = string.IsNullOrWhiteSpace(_options.Currency)
            ? "vnd"
            : _options.Currency.Trim().ToLowerInvariant();
        var decimals = _options.CurrencyDecimals <= 0 ? 0 : _options.CurrencyDecimals;
        var amountMinor = Convert.ToInt64(
            Math.Round(amount * Convert.ToDecimal(Math.Pow(10, decimals)), MidpointRounding.AwayFromZero));

        if (amountMinor <= 0)
        {
            throw new InvalidOperationException("Payment amount must be greater than zero.");
        }

        if (durationMonths <= 0)
        {
            throw new InvalidOperationException("Subscription duration must be greater than zero.");
        }

        var customerOptions = new CustomerCreateOptions
        {
            Email = customerEmail,
            Name = customerName
        };

        if (!string.IsNullOrWhiteSpace(paymentMethodId))
        {
            customerOptions.PaymentMethod = paymentMethodId;
            customerOptions.InvoiceSettings = new CustomerInvoiceSettingsOptions
            {
                DefaultPaymentMethod = paymentMethodId
            };
        }

        var customer = await _customerService.CreateAsync(customerOptions, cancellationToken: cancellationToken);
        var product = await _productService.CreateAsync(
            new ProductCreateOptions
            {
                Name = string.IsNullOrWhiteSpace(subscriptionName)
                    ? "Subscription"
                    : subscriptionName.Trim()
            },
            cancellationToken: cancellationToken);
        var createOptions = new SubscriptionCreateOptions
        {
            Customer = customer.Id,
            PaymentBehavior = "default_incomplete",
            Items = new List<SubscriptionItemOptions>
            {
                new()
                {
                    PriceData = new SubscriptionItemPriceDataOptions
                    {
                        Currency = currency,
                        UnitAmount = amountMinor,
                        Product = product.Id,
                        Recurring = new SubscriptionItemPriceDataRecurringOptions
                        {
                            Interval = "month",
                            IntervalCount = durationMonths
                        }
                    }
                }
            },
            Metadata = metadata.Count == 0 ? null : new Dictionary<string, string>(metadata),
            Expand = new List<string> { "latest_invoice.payment_intent" }
        };

        var subscription = await _subscriptionService.CreateAsync(createOptions, cancellationToken: cancellationToken);
        var paymentIntent = subscription.LatestInvoice?.PaymentIntent;

        return new StripeSubscriptionResult(
            subscription.Id,
            subscription.Status,
            paymentIntent?.Id,
            paymentIntent?.ClientSecret,
            currency,
            amountMinor);
    }

    public async Task<StripeCheckoutStatusResult> GetCheckoutStatusAsync(
        string? paymentIntentId,
        string? stripeSubscriptionId,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(stripeSubscriptionId))
        {
            var subscription = await _subscriptionService.GetAsync(
                stripeSubscriptionId,
                new SubscriptionGetOptions
                {
                    Expand = new List<string> { "latest_invoice.payment_intent" }
                },
                cancellationToken: cancellationToken);

            var invoice = subscription.LatestInvoice;
            var intent = invoice?.PaymentIntent;
            var status = NormalizeCheckoutStatus(subscription.Status, invoice?.Status, intent?.Status);

            return new StripeCheckoutStatusResult(
                status,
                IsSuccessStatus(status),
                IsTerminalStatus(status),
                intent?.Id,
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
                null);
        }

        throw new InvalidOperationException("Stripe payment identifiers are missing.");
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

        if (MatchesStatus(subscriptionStatus, "canceled", "cancelled", "unpaid", "incomplete_expired") ||
            MatchesStatus(invoiceStatus, "void", "uncollectible") ||
            MatchesStatus(paymentIntentStatus, "canceled", "cancelled"))
        {
            return "failed";
        }

        if (MatchesStatus(paymentIntentStatus, "processing"))
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

