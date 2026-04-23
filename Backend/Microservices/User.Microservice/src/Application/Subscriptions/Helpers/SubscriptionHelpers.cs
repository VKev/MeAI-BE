using Domain.Entities;

namespace Application.Subscriptions.Helpers;

internal static class SubscriptionHelpers
{
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
}
