using Application.Notifications.Commands;
using FluentValidation;

namespace Application.Notifications.Validators;

public sealed class MarkAllNotificationsAsReadCommandValidator : AbstractValidator<MarkAllNotificationsAsReadCommand>
{
    public MarkAllNotificationsAsReadCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}
