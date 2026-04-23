using Application.Subscriptions.Queries;
using FluentValidation;

namespace Application.Subscriptions.Validators;

public sealed class GetAdminUserSubscriptionByIdQueryValidator
    : AbstractValidator<GetAdminUserSubscriptionByIdQuery>
{
    public GetAdminUserSubscriptionByIdQueryValidator()
    {
        RuleFor(query => query.UserSubscriptionId)
            .NotEmpty();
    }
}
