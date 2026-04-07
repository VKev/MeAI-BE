namespace Application.Subscriptions.Models;

public sealed record PurchaseSubscriptionResponse(
    Guid SubscriptionId,
    float Cost,
    string Currency,
    decimal Amount,
    decimal CreditApplied,
    string? PaymentIntentId,
    string? ClientSecret,
    string Status,
    string? StripeSubscriptionId,
    bool Renew,
    Guid TransactionId,
    bool SubscriptionActivated,
    bool ScheduledChangeCreated,
    Guid? UserSubscriptionId,
    string ChangeType,
    DateTime? EffectiveDate,
    bool RequiresPayment);
