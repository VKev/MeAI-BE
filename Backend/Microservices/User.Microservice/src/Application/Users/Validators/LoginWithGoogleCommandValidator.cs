using Application.Users.Commands;
using FluentValidation;

namespace Application.Users.Validators;

public sealed class LoginWithGoogleCommandValidator : AbstractValidator<LoginWithGoogleCommand>
{
    public LoginWithGoogleCommandValidator()
    {
        RuleFor(x => x.IdToken)
            .NotEmpty();
    }
}
