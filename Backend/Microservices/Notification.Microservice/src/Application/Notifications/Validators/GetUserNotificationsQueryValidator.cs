using Application.Notifications.Queries;
using FluentValidation;

namespace Application.Notifications.Validators;

public sealed class GetUserNotificationsQueryValidator : AbstractValidator<GetUserNotificationsQuery>
{
    public GetUserNotificationsQueryValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Limit).InclusiveBetween(1, 100);
    }
}
