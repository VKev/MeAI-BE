using Application.Users.Commands;
using FluentValidation;

namespace Application.Users.Validators;

public sealed class ActivateUserCommandValidator : AbstractValidator<ActivateUserCommand>
{
    public ActivateUserCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty();
    }
}
