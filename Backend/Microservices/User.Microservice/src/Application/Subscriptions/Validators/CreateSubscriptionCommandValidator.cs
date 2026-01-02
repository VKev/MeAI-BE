using Application.Subscriptions.Commands;
using FluentValidation;

namespace Application.Subscriptions.Validators;

public sealed class CreateSubscriptionCommandValidator : AbstractValidator<CreateSubscriptionCommand>
{
    public CreateSubscriptionCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Subscription name is required.");

        RuleFor(x => x.Cost)
            .NotNull()
            .WithMessage("Subscription cost is required.")
            .GreaterThanOrEqualTo(0);

        RuleFor(x => x.DurationMonths)
            .GreaterThan(0)
            .WithMessage("Subscription duration must be greater than zero.");

        RuleFor(x => x.MeAiCoin)
            .GreaterThanOrEqualTo(0)
            .When(x => x.MeAiCoin.HasValue);

        RuleFor(x => x.Limits!)
            .SetValidator(new SubscriptionLimitsValidator())
            .When(x => x.Limits != null);
    }
}
