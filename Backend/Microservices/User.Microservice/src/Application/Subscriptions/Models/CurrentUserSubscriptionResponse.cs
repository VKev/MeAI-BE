namespace Application.Subscriptions.Models;

public sealed record CurrentUserSubscriptionResponse(
    Guid UserSubscriptionId,
    Guid SubscriptionId,
    string? SubscriptionName,
    DateTime? ActiveDate,
    DateTime? EndDate,
    string? Status,
    string DisplayStatus,
    bool IsCurrent,
    bool IsActive,
    bool IsScheduled);
