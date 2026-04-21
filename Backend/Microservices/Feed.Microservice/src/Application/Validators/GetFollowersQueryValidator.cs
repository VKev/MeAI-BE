using Application.Follows.Queries;
using FluentValidation;

namespace Application.Validators;

public sealed class GetFollowersQueryValidator : AbstractValidator<GetFollowersQuery>
{
    public GetFollowersQueryValidator()
    {
        RuleFor(query => query.UserId)
            .NotEmpty();

        RuleFor(query => query)
            .Must(HaveValidCursorPair)
            .WithMessage("cursorCreatedAt and cursorId must be provided together.");
    }

    private static bool HaveValidCursorPair(GetFollowersQuery query)
    {
        return query.CursorCreatedAt.HasValue == query.CursorId.HasValue;
    }
}
