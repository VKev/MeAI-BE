using Application.Subscriptions.Commands;
using FluentValidation;

namespace Application.Subscriptions.Validators;

public sealed class UpdateSubscriptionCommandValidator : AbstractValidator<UpdateSubscriptionCommand>
{
    public UpdateSubscriptionCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty();

        RuleFor(x => x.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .When(x => x.Name != null)
            .WithMessage("Subscription name cannot be empty.");

        RuleFor(x => x.MeAiCoin)
            .GreaterThanOrEqualTo(0)
            .When(x => x.MeAiCoin.HasValue);

        RuleFor(x => x.Limits)
            .SetValidator(new SubscriptionLimitsValidator())
            .When(x => x.Limits != null);
    }
}
