using Application.Comments.Commands;
using FluentValidation;

namespace Application.Validators;

public sealed class DeleteCommentCommandValidator : AbstractValidator<DeleteCommentCommand>
{
    public DeleteCommentCommandValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.CommentId).NotEmpty();
    }
}
