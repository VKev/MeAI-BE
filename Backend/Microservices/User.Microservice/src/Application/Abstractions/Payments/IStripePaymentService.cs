namespace Application.Abstractions.Payments;

public interface IStripePaymentService
{
    Task<StripePaymentIntentResult> CreatePaymentIntentAsync(
        decimal amount,
        string? paymentMethodId,
        IDictionary<string, string> metadata,
        CancellationToken cancellationToken = default);
}

public sealed record StripePaymentIntentResult(
    string PaymentIntentId,
    string? ClientSecret,
    string Status,
    string Currency,
    long Amount);
