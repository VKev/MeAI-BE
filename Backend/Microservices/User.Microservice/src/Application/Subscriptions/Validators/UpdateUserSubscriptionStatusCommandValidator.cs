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

        RuleFor(command => command.Reason)
            .Must(reason => !string.IsNullOrWhiteSpace(reason))
            .WithMessage("'Reason' must not be empty.")
            .MaximumLength(5000);
    }
}
