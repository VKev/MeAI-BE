using Application.Abstractions.Payments;
using Infrastructure.Configs;
using Microsoft.Extensions.Options;
using Stripe;

namespace Infrastructure.Payments;

public sealed class StripePaymentService : IStripePaymentService
{
    private readonly StripeOptions _options;
    private readonly PaymentIntentService _paymentIntentService;

    public StripePaymentService(IOptions<StripeOptions> options)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            throw new InvalidOperationException("Stripe secret key is not configured.");
        }

        var stripeClient = new StripeClient(_options.SecretKey);
        _paymentIntentService = new PaymentIntentService(stripeClient);
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
}
