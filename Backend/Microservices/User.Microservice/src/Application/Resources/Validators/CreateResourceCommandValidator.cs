using Application.Resources.Commands;
using FluentValidation;

namespace Application.Resources.Validators;

public sealed class CreateResourceCommandValidator : AbstractValidator<CreateResourceCommand>
{
    public CreateResourceCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Link).NotEmpty();
    }
}
