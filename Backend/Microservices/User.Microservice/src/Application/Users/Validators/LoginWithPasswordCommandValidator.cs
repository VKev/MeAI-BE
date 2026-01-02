using Application.Users.Commands;
using FluentValidation;

namespace Application.Users.Validators;

public sealed class LoginWithPasswordCommandValidator : AbstractValidator<LoginWithPasswordCommand>
{
    public LoginWithPasswordCommandValidator()
    {
        RuleFor(x => x.EmailOrUsername)
            .NotEmpty()
            .MinimumLength(5)
            .WithMessage("Email or username must be at least 5 characters");

        RuleFor(x => x.Password)
            .NotEmpty();
    }
}
