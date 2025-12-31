using Application.Resources.Commands;
using FluentValidation;

namespace Application.Resources.Validators;

public sealed class UpdateResourceCommandValidator : AbstractValidator<UpdateResourceCommand>
{
    public UpdateResourceCommandValidator()
    {
        RuleFor(x => x.ResourceId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Link).NotEmpty();
    }
}
