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
}
