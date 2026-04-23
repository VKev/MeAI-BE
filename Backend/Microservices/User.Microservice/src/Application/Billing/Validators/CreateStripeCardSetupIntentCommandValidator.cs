using Application.Billing.Commands;
using FluentValidation;

namespace Application.Billing.Validators;

public sealed class CreateStripeCardSetupIntentCommandValidator
    : AbstractValidator<CreateStripeCardSetupIntentCommand>
{
    public CreateStripeCardSetupIntentCommandValidator()
    {
        RuleFor(command => command.UserId)
            .NotEmpty();
    }
}
