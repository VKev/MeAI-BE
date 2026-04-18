using Application.Posts.Queries;
using FluentValidation;

namespace Application.Validators;

public sealed class GetFeedPostsQueryValidator : AbstractValidator<GetFeedPostsQuery>
{
    public GetFeedPostsQueryValidator()
    {
        RuleFor(query => query.UserId)
            .NotEmpty();

        RuleFor(query => query)
            .Must(HaveValidCursorPair)
            .WithMessage("cursorCreatedAt and cursorId must be provided together.");
    }

    private static bool HaveValidCursorPair(GetFeedPostsQuery query)
    {
        return query.CursorCreatedAt.HasValue == query.CursorId.HasValue;
    }
}
