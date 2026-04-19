using Application.Posts.Queries;
using FluentValidation;

namespace Application.Validators;

public sealed class GetPostsByUsernameQueryValidator : AbstractValidator<GetPostsByUsernameQuery>
{
    public GetPostsByUsernameQueryValidator()
    {
        RuleFor(query => query.Username)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(query => query)
            .Must(HaveValidCursorPair)
            .WithMessage("cursorCreatedAt and cursorId must be provided together.");
    }

    private static bool HaveValidCursorPair(GetPostsByUsernameQuery query)
    {
        return query.CursorCreatedAt.HasValue == query.CursorId.HasValue;
    }
}
