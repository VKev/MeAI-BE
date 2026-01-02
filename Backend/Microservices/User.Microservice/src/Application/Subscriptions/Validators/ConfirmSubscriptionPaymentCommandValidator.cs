using Application.Subscriptions.Commands;
using FluentValidation;

namespace Application.Subscriptions.Validators;

public sealed class ConfirmSubscriptionPaymentCommandValidator
    : AbstractValidator<ConfirmSubscriptionPaymentCommand>
{
    public ConfirmSubscriptionPaymentCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty();

        RuleFor(x => x.SubscriptionId)
            .NotEmpty();

        RuleFor(x => x.Status)
            .NotEmpty()
            .WithMessage("Payment status is required.");
    }
}
