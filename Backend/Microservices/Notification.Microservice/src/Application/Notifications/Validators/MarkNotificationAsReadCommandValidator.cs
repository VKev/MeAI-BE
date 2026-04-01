using Application.Notifications.Commands;
using FluentValidation;

namespace Application.Notifications.Validators;

public sealed class MarkNotificationAsReadCommandValidator : AbstractValidator<MarkNotificationAsReadCommand>
{
    public MarkNotificationAsReadCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.UserNotificationId).NotEmpty();
    }
}
