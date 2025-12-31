using Application.Users.Commands;
using FluentValidation;

namespace Application.Users.Validators;

public sealed class LoginWithPasswordCommandValidator : AbstractValidator<LoginWithPasswordCommand>
{
    public LoginWithPasswordCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress();

        RuleFor(x => x.Password)
            .NotEmpty();
    }
}
