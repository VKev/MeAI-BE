using Application.Posts.Commands;
using FluentValidation;

namespace Application.Validators;

public sealed class UnlikePostCommandValidator : AbstractValidator<UnlikePostCommand>
{
    public UnlikePostCommandValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.PostId).NotEmpty();
    }
}
