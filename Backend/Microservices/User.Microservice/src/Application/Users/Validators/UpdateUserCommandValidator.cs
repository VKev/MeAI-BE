using Application.Users.Commands;
using FluentValidation;

namespace Application.Users.Validators;

public sealed class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        When(x => x.Email != null, () =>
        {
            RuleFor(x => x.Email).NotEmpty().EmailAddress();
        });
        When(x => x.Username != null, () =>
        {
            RuleFor(x => x.Username).NotEmpty();
        });
    }
}
