using Application.Comments.Queries;
using FluentValidation;

namespace Application.Validators;

public sealed class GetCommentRepliesQueryValidator : AbstractValidator<GetCommentRepliesQuery>
{
    public GetCommentRepliesQueryValidator()
    {
        RuleFor(query => query.CommentId)
            .NotEmpty();

        RuleFor(query => query)
            .Must(HaveValidCursorPair)
            .WithMessage("cursorCreatedAt and cursorId must be provided together.");
    }

    private static bool HaveValidCursorPair(GetCommentRepliesQuery query)
    {
        return query.CursorCreatedAt.HasValue == query.CursorId.HasValue;
    }
}
