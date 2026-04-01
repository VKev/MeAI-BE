namespace Application.Subscriptions.Models;

public static class SubscriptionChangeTypes
{
    public const string NewPurchase = "new_purchase";
    public const string Upgrade = "upgrade";
    public const string ScheduledChange = "scheduled_change";

    public static string FromTransactionType(string? transactionType)
    {
        if (string.Equals(transactionType, "SubscriptionUpgrade", StringComparison.OrdinalIgnoreCase))
        {
            return Upgrade;
        }

        if (string.Equals(transactionType, "SubscriptionScheduledChange", StringComparison.OrdinalIgnoreCase))
        {
            return ScheduledChange;
        }

        return NewPurchase;
    }

    public static string ToTransactionType(string changeType)
    {
        if (string.Equals(changeType, Upgrade, StringComparison.OrdinalIgnoreCase))
        {
            return "SubscriptionUpgrade";
        }

        if (string.Equals(changeType, ScheduledChange, StringComparison.OrdinalIgnoreCase))
        {
            return "SubscriptionScheduledChange";
        }

        return "SubscriptionPurchase";
    }
}
