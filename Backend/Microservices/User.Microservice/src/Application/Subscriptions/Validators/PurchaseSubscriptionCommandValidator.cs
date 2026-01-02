using Application.Subscriptions.Commands;
using FluentValidation;

namespace Application.Subscriptions.Validators;

public sealed class PurchaseSubscriptionCommandValidator : AbstractValidator<PurchaseSubscriptionCommand>
{
    public PurchaseSubscriptionCommandValidator()
    {
        RuleFor(x => x.SubscriptionId)
            .NotEmpty();

        RuleFor(x => x.UserId)
            .NotEmpty();
    }
}
