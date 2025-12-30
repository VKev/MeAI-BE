using Domain.Entities;
using FluentValidation;

namespace Application.Subscriptions.Validators;

internal sealed class SubscriptionLimitsValidator : AbstractValidator<SubscriptionLimits>
{
    public SubscriptionLimitsValidator()
    {
        RuleFor(x => x.NumberOfSocialAccounts)
            .GreaterThanOrEqualTo(0)
            .When(x => x.NumberOfSocialAccounts.HasValue);

        RuleFor(x => x.RateLimitForContentCreation)
            .GreaterThanOrEqualTo(0)
            .When(x => x.RateLimitForContentCreation.HasValue);

        RuleFor(x => x.NumberOfWorkspaces)
            .GreaterThanOrEqualTo(0)
            .When(x => x.NumberOfWorkspaces.HasValue);
    }
}
