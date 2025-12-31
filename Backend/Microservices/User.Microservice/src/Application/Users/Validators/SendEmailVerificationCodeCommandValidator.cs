using Application.Users.Commands;
using FluentValidation;

namespace Application.Users.Validators;

public sealed class SendEmailVerificationCodeCommandValidator
    : AbstractValidator<SendEmailVerificationCodeCommand>
{
    public SendEmailVerificationCodeCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress();
    }
}
