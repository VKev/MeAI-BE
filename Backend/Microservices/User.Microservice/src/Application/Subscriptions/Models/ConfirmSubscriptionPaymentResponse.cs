namespace Application.Subscriptions.Models;

public sealed record ConfirmSubscriptionPaymentResponse(
    string ChangeType,
    bool SubscriptionActivated,
    bool ScheduledChangeCreated,
    Guid? UserSubscriptionId,
    DateTime? EffectiveDate);
