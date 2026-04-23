using Application.Billing.Commands;
using FluentValidation;

namespace Application.Billing.Validators;

public sealed class SetDefaultStripeCardCommandValidator
    : AbstractValidator<SetDefaultStripeCardCommand>
{
    public SetDefaultStripeCardCommandValidator()
    {
        RuleFor(command => command.UserId)
            .NotEmpty();

        RuleFor(command => command.PaymentMethodId)
            .NotEmpty()
            .MaximumLength(255);
    }
}
