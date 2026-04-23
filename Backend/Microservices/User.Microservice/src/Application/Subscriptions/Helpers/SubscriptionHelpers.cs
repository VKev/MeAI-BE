using Domain.Entities;

namespace Application.Subscriptions.Helpers;

internal static class SubscriptionHelpers
{
    public const string AutoRenewEnabled = "auto_renew_enabled";
    public const string AutoRenewDisabled = "auto_renew_disabled";
    public const string AutoRenewNotRecurring = "not_recurring";
    public const string AutoRenewScheduledChange = "scheduled_change";
    public const string AutoRenewPlanDeleted = "plan_deleted";
    public const string AutoRenewPlanInactive = "plan_inactive";
    public const string AutoRenewNotCurrent = "not_current";

    public static string? NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return name.Trim();
    }

    public static bool ApplyLimitsPatch(SubscriptionLimits target, SubscriptionLimits patch)
    {
        var updated = false;

        if (patch.NumberOfSocialAccounts.HasValue)
        {
            target.NumberOfSocialAccounts = patch.NumberOfSocialAccounts;
            updated = true;
        }

        if (patch.RateLimitForContentCreation.HasValue)
        {
            target.RateLimitForContentCreation = patch.RateLimitForContentCreation;
            updated = true;
        }

        if (patch.NumberOfWorkspaces.HasValue)
        {
            target.NumberOfWorkspaces = patch.NumberOfWorkspaces;
            updated = true;
        }

        return updated;
    }

    public static string ResolveDisplayStatus(string? status, Subscription? subscription)
    {
        if (string.Equals(status, "non_renewable", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "non-renewable", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "nonrenewable", StringComparison.OrdinalIgnoreCase))
        {
            return "No recurring";
        }

        if (subscription?.IsDeleted == true)
        {
            return "Plan deleted - no recurring";
        }

        if (subscription?.IsActive == false)
        {
            return "Plan inactive - no recurring";
        }

        if (string.IsNullOrWhiteSpace(status))
        {
            return "Active";
        }

        return status.Trim();
    }

    public static bool ResolveAutoRenewEnabled(
        UserSubscription userSubscription,
        Subscription? subscription,
        bool isScheduled)
    {
        return ResolveAutoRenewStatus(userSubscription, subscription, isScheduled) == AutoRenewEnabled;
    }

    public static string ResolveAutoRenewStatus(
        UserSubscription userSubscription,
        Subscription? subscription,
        bool isScheduled)
    {
        if (isScheduled)
        {
            return AutoRenewScheduledChange;
        }

        if (subscription?.IsDeleted == true)
        {
            return AutoRenewPlanDeleted;
        }

        if (subscription?.IsActive == false)
        {
            return AutoRenewPlanInactive;
        }

        if (IsNonRenewableStatus(userSubscription.Status))
        {
            return AutoRenewDisabled;
        }

        if (!IsActiveStatus(userSubscription.Status))
        {
            return AutoRenewNotCurrent;
        }

        return string.IsNullOrWhiteSpace(userSubscription.StripeSubscriptionId)
            ? AutoRenewNotRecurring
            : AutoRenewEnabled;
    }

    public static bool IsActiveStatus(string? status)
    {
        return string.IsNullOrWhiteSpace(status) ||
               string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsNonRenewableStatus(string? status)
    {
        return string.Equals(status, "non_renewable", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, "non-renewable", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, "nonrenewable", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsScheduledStatus(string? status)
    {
        return string.Equals(status, "Scheduled", StringComparison.OrdinalIgnoreCase);
    }

    public static bool CanSetAutoRenew(string? status)
    {
        return IsActiveStatus(status) || IsNonRenewableStatus(status);
    }
}
