using Application.Comments.Queries;
using FluentValidation;

namespace Application.Validators;

public sealed class GetCommentsByPostIdQueryValidator : AbstractValidator<GetCommentsByPostIdQuery>
{
    public GetCommentsByPostIdQueryValidator()
    {
        RuleFor(query => query.PostId)
            .NotEmpty();

        RuleFor(query => query)
            .Must(HaveValidCursorPair)
            .WithMessage("cursorCreatedAt and cursorId must be provided together.");
    }

    private static bool HaveValidCursorPair(GetCommentsByPostIdQuery query)
    {
        return query.CursorCreatedAt.HasValue == query.CursorId.HasValue;
    }
}
