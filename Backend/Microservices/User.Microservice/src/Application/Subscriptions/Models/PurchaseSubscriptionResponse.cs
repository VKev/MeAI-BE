namespace Application.Subscriptions.Models;

public sealed record PurchaseSubscriptionResponse(
    Guid SubscriptionId,
    float Cost,
    string Currency,
    long Amount,
    string? PaymentIntentId,
    string? ClientSecret,
    string Status,
    string? StripeSubscriptionId,
    bool Renew,
    Guid TransactionId,
    bool SubscriptionActivated,
    Guid? UserSubscriptionId);
