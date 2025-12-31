using Application.Resources.Commands;
using FluentValidation;

namespace Application.Resources.Validators;

public sealed class DeleteResourceCommandValidator : AbstractValidator<DeleteResourceCommand>
{
    public DeleteResourceCommandValidator()
    {
        RuleFor(x => x.ResourceId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
    }
}
