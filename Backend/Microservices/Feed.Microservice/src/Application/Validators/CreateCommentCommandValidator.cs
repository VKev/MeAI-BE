using Application.Comments.Commands;
using FluentValidation;

namespace Application.Validators;

public sealed class CreateCommentCommandValidator : AbstractValidator<CreateCommentCommand>
{
    public CreateCommentCommandValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.PostId).NotEmpty();
        RuleFor(command => command.Content)
            .NotEmpty()
            .MaximumLength(2000);
    }
}
