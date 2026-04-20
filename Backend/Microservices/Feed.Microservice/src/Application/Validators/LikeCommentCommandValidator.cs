using Application.Comments.Commands;
using FluentValidation;

namespace Application.Validators;

public sealed class LikeCommentCommandValidator : AbstractValidator<LikeCommentCommand>
{
    public LikeCommentCommandValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.CommentId).NotEmpty();
    }
}
