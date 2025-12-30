using Application.Subscriptions.Queries;
using FluentValidation;

namespace Application.Subscriptions.Validators;

public sealed class GetSubscriptionByIdQueryValidator : AbstractValidator<GetSubscriptionByIdQuery>
{
    public GetSubscriptionByIdQueryValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty();
    }
}
