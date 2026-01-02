using Application.Subscriptions.Commands;
using FluentValidation;

namespace Application.Subscriptions.Validators;

public sealed class PatchSubscriptionCommandValidator : AbstractValidator<PatchSubscriptionCommand>
{
    public PatchSubscriptionCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty();

        RuleFor(x => x)
            .Must(HasAnyChanges)
            .WithMessage("At least one field must be provided for patch.");

        RuleFor(x => x.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .When(x => x.Name != null)
            .WithMessage("Subscription name cannot be empty.");

        RuleFor(x => x.Cost)
            .GreaterThanOrEqualTo(0)
            .When(x => x.Cost.HasValue);

        RuleFor(x => x.DurationMonths)
            .GreaterThan(0)
            .When(x => x.DurationMonths.HasValue)
            .WithMessage("Subscription duration must be greater than zero.");

        RuleFor(x => x.MeAiCoin)
            .GreaterThanOrEqualTo(0)
            .When(x => x.MeAiCoin.HasValue);

        RuleFor(x => x.Limits!)
            .SetValidator(new SubscriptionLimitsValidator())
            .When(x => x.Limits != null);
    }

    private static bool HasAnyChanges(PatchSubscriptionCommand command)
    {
        if (command.Name != null || command.Cost.HasValue || command.DurationMonths.HasValue || command.MeAiCoin.HasValue)
        {
            return true;
        }

        if (command.Limits == null)
        {
            return false;
        }

        return command.Limits.NumberOfSocialAccounts.HasValue
            || command.Limits.RateLimitForContentCreation.HasValue
            || command.Limits.NumberOfWorkspaces.HasValue;
    }
}
