using Application.Follows.Commands;
using FluentValidation;

namespace Application.Validators;

public sealed class FollowUserCommandValidator : AbstractValidator<FollowUserCommand>
{
    public FollowUserCommandValidator()
    {
        RuleFor(command => command.FollowerId).NotEmpty();
        RuleFor(command => command.FolloweeId).NotEmpty();
    }
}
