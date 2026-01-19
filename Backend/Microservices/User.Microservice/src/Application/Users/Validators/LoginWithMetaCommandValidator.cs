using Application.Users.Commands;
using FluentValidation;

namespace Application.Users.Validators;

public sealed class LoginWithMetaCommandValidator : AbstractValidator<LoginWithMetaCommand>
{
    public LoginWithMetaCommandValidator()
    {
        RuleFor(x => x.AccessToken)
            .NotEmpty();
    }
}
