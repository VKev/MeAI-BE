using Application.Comments.Commands;
using FluentValidation;

namespace Application.Validators;

public sealed class ReplyToCommentCommandValidator : AbstractValidator<ReplyToCommentCommand>
{
    public ReplyToCommentCommandValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.CommentId).NotEmpty();
        RuleFor(command => command.Content)
            .NotEmpty()
            .MaximumLength(2000);
    }
}
