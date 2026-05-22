using Application.Billing.Commands;
using FluentValidation;

namespace Application.Billing.Validators;

public sealed class DeleteStripeCardCommandValidator
    : AbstractValidator<DeleteStripeCardCommand>
{
    public DeleteStripeCardCommandValidator()
    {
        RuleFor(command => command.UserId)
            .NotEmpty();

        RuleFor(command => command.PaymentMethodId)
            .NotEmpty()
            .MaximumLength(255);
    }
}
