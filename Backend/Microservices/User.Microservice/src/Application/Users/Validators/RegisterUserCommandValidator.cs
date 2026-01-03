using Application.Users.Commands;
using FluentValidation;

namespace Application.Users.Validators;

public sealed class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserCommandValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty()
            .MinimumLength(5)
            .WithMessage("Username must be at least 5 characters");

        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress();

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(6)
            .WithMessage("Password must be at least 6 characters")
            .Matches(@"[A-Z]")
            .WithMessage("Password must include an uppercase letter")
            .Matches(@"\d")
            .WithMessage("Password must include a number")
            .Matches(@"[^A-Za-z0-9]")
            .WithMessage("Password must include a special character");

        RuleFor(x => x.Code)
            .NotEmpty()
            .Length(6);
    }
}
