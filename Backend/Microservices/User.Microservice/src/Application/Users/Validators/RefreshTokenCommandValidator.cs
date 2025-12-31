using Application.Users.Commands;
using FluentValidation;

namespace Application.Users.Validators;

public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty();
    }
}
