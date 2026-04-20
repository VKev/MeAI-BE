using Application.Comments.Commands;
using FluentValidation;

namespace Application.Validators;

public sealed class UnlikeCommentCommandValidator : AbstractValidator<UnlikeCommentCommand>
{
    public UnlikeCommentCommandValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.CommentId).NotEmpty();
    }
}
