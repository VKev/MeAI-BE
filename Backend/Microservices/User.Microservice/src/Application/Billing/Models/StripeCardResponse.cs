namespace Application.Billing.Models;

public sealed record StripeCardResponse(
    string PaymentMethodId,
    string? Brand,
    string? Last4,
    long? ExpMonth,
    long? ExpYear,
    string? Funding,
    string? Country,
    string? CardholderName,
    bool IsDefault,
    bool IsExpired);

public sealed record StripeCardSetupIntentResponse(
    string SetupIntentId,
    string ClientSecret,
    string StripeCustomerId);
