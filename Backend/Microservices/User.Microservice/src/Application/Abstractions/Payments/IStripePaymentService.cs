namespace Application.Abstractions.Payments;

public interface IStripePaymentService
{
    Task<StripePaymentIntentResult> CreatePaymentIntentAsync(
        decimal amount,
        string? paymentMethodId,
        IDictionary<string, string> metadata,
        CancellationToken cancellationToken = default);

    Task<StripeSubscriptionResult> CreateSubscriptionAsync(
        decimal amount,
        int durationMonths,
        string? paymentMethodId,
        string? customerEmail,
        string? customerName,
        string? subscriptionName,
        IDictionary<string, string> metadata,
        CancellationToken cancellationToken = default);
}

public sealed record StripePaymentIntentResult(
    string PaymentIntentId,
    string? ClientSecret,
    string Status,
    string Currency,
    long Amount);

public sealed record StripeSubscriptionResult(
    string SubscriptionId,
    string Status,
    string? PaymentIntentId,
    string? ClientSecret,
    string Currency,
    long Amount);
