using Application.Subscriptions.Commands;
using FluentValidation;

namespace Application.Subscriptions.Validators;

public sealed class SetAdminUserSubscriptionAutoRenewCommandValidator
    : AbstractValidator<SetAdminUserSubscriptionAutoRenewCommand>
{
    public SetAdminUserSubscriptionAutoRenewCommandValidator()
    {
        RuleFor(command => command.UserSubscriptionId)
            .NotEmpty();

        RuleFor(command => command.Reason)
            .NotEmpty()
            .MaximumLength(5000);
    }
}
