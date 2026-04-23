using Application.Subscriptions.Commands;
using FluentValidation;

namespace Application.Subscriptions.Validators;

public sealed class SetSubscriptionAutoRenewCommandValidator
    : AbstractValidator<SetSubscriptionAutoRenewCommand>
{
    public SetSubscriptionAutoRenewCommandValidator()
    {
        RuleFor(command => command.UserId)
            .NotEmpty();
    }
}
