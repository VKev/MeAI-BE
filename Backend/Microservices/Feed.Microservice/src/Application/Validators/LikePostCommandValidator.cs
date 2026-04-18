using Application.Posts.Commands;
using FluentValidation;

namespace Application.Validators;

public sealed class LikePostCommandValidator : AbstractValidator<LikePostCommand>
{
    public LikePostCommandValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.PostId).NotEmpty();
    }
}
