using Application.Subscriptions.Commands;
using FluentValidation;

namespace Application.Subscriptions.Validators;

public sealed class UpdateUserSubscriptionStatusCommandValidator
    : AbstractValidator<UpdateUserSubscriptionStatusCommand>
{
    public UpdateUserSubscriptionStatusCommandValidator()
    {
        RuleFor(command => command.UserSubscriptionId)
            .NotEmpty();

        RuleFor(command => command.Status)
            .NotEmpty()
            .MaximumLength(64);
    }
}
