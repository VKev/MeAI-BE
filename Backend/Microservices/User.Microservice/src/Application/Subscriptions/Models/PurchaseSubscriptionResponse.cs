namespace Application.Subscriptions.Models;

public sealed record PurchaseSubscriptionResponse(
    Guid SubscriptionId,
    float Cost,
    string Currency,
    long Amount,
    string PaymentIntentId,
    string? ClientSecret,
    string Status,
    Guid TransactionId,
    bool SubscriptionActivated,
    Guid? UserSubscriptionId);
