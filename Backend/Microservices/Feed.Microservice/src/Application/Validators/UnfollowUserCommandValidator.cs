using Application.Follows.Commands;
using FluentValidation;

namespace Application.Validators;

public sealed class UnfollowUserCommandValidator : AbstractValidator<UnfollowUserCommand>
{
    public UnfollowUserCommandValidator()
    {
        RuleFor(command => command.FollowerId).NotEmpty();
        RuleFor(command => command.FolloweeId).NotEmpty();
    }
}
