using Application.Users.Commands;
using FluentValidation;

namespace Application.Users.Validators;

public sealed class SetUserRoleCommandValidator : AbstractValidator<SetUserRoleCommand>
{
    public SetUserRoleCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Role).NotEmpty();
    }
}
